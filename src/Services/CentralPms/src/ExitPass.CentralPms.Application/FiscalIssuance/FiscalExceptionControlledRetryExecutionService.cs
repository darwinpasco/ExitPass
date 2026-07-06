using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionControlledRetryExecutionService :
    IFiscalExceptionControlledRetryExecutionService
{
    private readonly FiscalExceptionControlledRetryExecutionOptions _options;
    private readonly IPosServerFiscalDocumentRequestMapper _requestMapper;
    private readonly IFiscalSemanticRequestHashCalculator _semanticRequestHashCalculator;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _liveIntegrationService;
    private readonly IFiscalExceptionControlledRetryExecutionAuditRepository? _auditRepository;

    public FiscalExceptionControlledRetryExecutionService(
        FiscalExceptionControlledRetryExecutionOptions options,
        IPosServerFiscalDocumentRequestMapper requestMapper,
        IFiscalSemanticRequestHashCalculator semanticRequestHashCalculator,
        IFiscalIssuancePosServerLiveIntegrationService liveIntegrationService,
        IFiscalExceptionControlledRetryExecutionAuditRepository? auditRepository)
    {
        _options = options ?? new FiscalExceptionControlledRetryExecutionOptions();
        _requestMapper = requestMapper;
        _semanticRequestHashCalculator = semanticRequestHashCalculator;
        _liveIntegrationService = liveIntegrationService;
        _auditRepository = auditRepository;
    }

    public async Task<FiscalExceptionControlledRetryExecutionResult> ExecuteAsync(
        FiscalExceptionControlledRetryExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);
        ArgumentNullException.ThrowIfNull(request.CommandPreparation);
        ArgumentNullException.ThrowIfNull(request.SchedulingPreparation);
        ArgumentNullException.ThrowIfNull(request.ExecutionPreparation);

        var attemptedAt = DateTimeOffset.UtcNow;
        var summary = request.Detail.Summary;

        if (!_options.EnableControlledRetryExecution)
        {
            return Result(
                request,
                FiscalExceptionControlledRetryExecutionStatus.Disabled,
                "controlled_retry_execution_disabled",
                "controlled_retry_execution_disabled_by_default",
                attemptedAt,
                completedAt: null,
                retryAttemptId: null,
                posServerResult: null,
                posServerPostCalled: false,
                fiscalReferenceSuccessRecorded: false);
        }

        if (_auditRepository is null)
        {
            return Result(
                request,
                FiscalExceptionControlledRetryExecutionStatus.Unavailable,
                "controlled_retry_execution_audit_persistence_unavailable",
                "controlled_retry_execution_unavailable_audit_persistence_unavailable",
                attemptedAt,
                completedAt: null,
                retryAttemptId: null,
                posServerResult: null,
                posServerPostCalled: false,
                fiscalReferenceSuccessRecorded: false);
        }

        var gateResult = ValidateGates(request, attemptedAt);
        if (gateResult is not null)
        {
            return await RecordAndAttachAuditAsync(request, gateResult, null, cancellationToken);
        }

        if (request.DryRunOnly)
        {
            return await RecordAndAttachAuditAsync(
                request,
                Result(
                    request,
                    FiscalExceptionControlledRetryExecutionStatus.DryRunReady,
                    blockReasonCode: null,
                    "controlled_retry_execution_dry_run_ready_no_pos_server_post",
                    attemptedAt,
                    completedAt: null,
                    retryAttemptId: null,
                    posServerResult: null,
                    posServerPostCalled: false,
                    fiscalReferenceSuccessRecorded: false),
                null,
                cancellationToken);
        }

        var recordingContext = new PosServerCreateResultRecordingContext(
            UpstreamFinalityReference: summary.UpstreamFinalityReference,
            SitePosServerId: summary.SitePosServerId,
            FiscalDocumentTypeCodeId: request.FiscalContext!.FiscalDocumentTypeCodeId,
            CorrelationId: request.CorrelationId ?? request.Detail.CorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow,
            ServiceIdentityId: request.ServiceIdentityId);

        FiscalIssuancePosServerLiveIntegrationResult liveResult;
        try
        {
            liveResult = await _liveIntegrationService.TryIssueFiscalDocumentViaPosServerAsync(
                summary.FiscalIssuanceReferenceId,
                request.FiscalContext!,
                recordingContext,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await RecordAndAttachAuditAsync(
                request,
                Result(
                    request,
                    FiscalExceptionControlledRetryExecutionStatus.Unknown,
                    "pos_server_retry_execution_unknown",
                    "controlled_retry_execution_unknown_pos_server_or_reference_application_failed",
                    attemptedAt,
                    completedAt: DateTimeOffset.UtcNow,
                    retryAttemptId: null,
                    posServerResult: null,
                    posServerPostCalled: true,
                    fiscalReferenceSuccessRecorded: false),
                null,
                cancellationToken);
        }

        var status = ResolveExecutionStatus(liveResult);
        var posServerPostCalled = liveResult.MappedRequest is not null && liveResult.PosServerResult is not null;
        var fiscalReferenceSuccessRecorded = liveResult.FiscalIssuanceReference?.FiscalIssuanceState is
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded or
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed;

        var result = Result(
            request,
            status,
            ToBlockReason(liveResult),
            ToSafeSummary(status, liveResult),
            attemptedAt,
            completedAt: DateTimeOffset.UtcNow,
            retryAttemptId: null,
            posServerResult: liveResult.PosServerResult,
            posServerPostCalled,
            fiscalReferenceSuccessRecorded);

        return await RecordAndAttachAuditAsync(request, result, liveResult.PosServerResult, cancellationToken);
    }

    private FiscalExceptionControlledRetryExecutionResult? ValidateGates(
        FiscalExceptionControlledRetryExecutionRequest request,
        DateTimeOffset attemptedAt)
    {
        var summary = request.Detail.Summary;

        if (!request.SingleRecordOnly || summary.FiscalIssuanceReferenceId == Guid.Empty)
        {
            return Blocked(request, "controlled_retry_execution_single_record_required", attemptedAt);
        }

        if (request.ServiceIdentityId is null || request.ServiceIdentityId == Guid.Empty)
        {
            return Blocked(request, "service_identity_required_for_controlled_retry_execution", attemptedAt);
        }

        if (string.IsNullOrWhiteSpace(request.ApprovalReference))
        {
            return Blocked(request, "approval_reference_required_for_controlled_retry_execution", attemptedAt);
        }

        if (request.ExecutionPreparation.DualControlRequired &&
            string.IsNullOrWhiteSpace(request.DualControlReference))
        {
            return Blocked(request, "dual_control_reference_required_for_controlled_retry_execution", attemptedAt);
        }

        if (request.ExecutionPreparation.Status !=
            FiscalExceptionRetryExecutionPreparationStatus.ReadyForExecutionWhenEnabled)
        {
            return Blocked(
                request,
                request.ExecutionPreparation.BlockReasonCode ?? "retry_execution_preparation_not_ready",
                attemptedAt);
        }

        if (request.ExecutionPreparation.PosServerReadinessStatus !=
            FiscalExceptionRetryExecutionPosServerReadinessStatus.Confirmed)
        {
            return Blocked(request, "pos_server_readiness_not_confirmed", attemptedAt);
        }

        if (request.CommandPreparation.Status != FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable ||
            request.CommandPreparation.Command is null ||
            request.CommandPreparation.Command.Executable ||
            request.CommandPreparation.RetryCommandPreparationAttemptId is null)
        {
            return Blocked(
                request,
                request.CommandPreparation.BlockReasonCode ?? "retry_command_preparation_not_safe",
                attemptedAt);
        }

        if (request.SchedulingPreparation.Status !=
                FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared ||
            request.SchedulingPreparation.Schedule is null ||
            request.SchedulingPreparation.Schedule.Executable ||
            request.SchedulingPreparation.RetrySchedulePreparationAttemptId is null)
        {
            return Blocked(
                request,
                request.SchedulingPreparation.BlockReasonCode ?? "retry_scheduling_preparation_missing",
                attemptedAt);
        }

        if (request.SchedulingPreparation.Schedule.RetryCommandPreparationAttemptId !=
            request.CommandPreparation.RetryCommandPreparationAttemptId)
        {
            return Blocked(request, "retry_command_preparation_audit_mismatch", attemptedAt);
        }

        if (summary.ReadbackAttemptCount is null or < 1 ||
            summary.ReadbackClassification != FiscalExceptionReadbackClassification.NotFound)
        {
            return Blocked(request, ToReadbackBlockReason(summary.ReadbackClassification), attemptedAt);
        }

        if (summary.RetryEligibilityDecision != FiscalExceptionRetryEligibilityDecision.Eligible ||
            summary.RetryEligibilityStatus != FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning)
        {
            return Blocked(
                request,
                summary.RetryBlockReasonCode ?? "retry_eligibility_not_eligible",
                attemptedAt);
        }

        if (summary.SemanticRequestHashAvailabilityStatus !=
                FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashValue))
        {
            return Blocked(request, "semantic_request_hash_required_but_missing", attemptedAt);
        }

        var semanticHashReadiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(summary);
        if (!FiscalExceptionSemanticHashReadinessPolicy.IsReady(semanticHashReadiness.Status))
        {
            return Blocked(
                request,
                semanticHashReadiness.BlockReasonCode ?? "semantic_hash_not_ready",
                attemptedAt,
                semanticHashReadiness.SafeSummary);
        }

        if (summary.IdempotencyContextAvailabilityStatus !=
                FiscalExceptionIdempotencyContextAvailabilityStatus.Available ||
            string.IsNullOrWhiteSpace(summary.UpstreamFinalityReference))
        {
            return Blocked(request, "idempotency_context_not_available", attemptedAt);
        }

        if (IsNewUpstreamFinalityReferenceRequested(
            summary.UpstreamFinalityReference,
            request.RequestedUpstreamFinalityReference))
        {
            return Blocked(request, "new_upstream_finality_reference_rejected", attemptedAt);
        }

        if (request.FiscalContext is null)
        {
            return Blocked(request, "original_fiscal_request_facts_unavailable", attemptedAt);
        }

        PosServerFiscalDocumentCreateRequest mappedRequest;
        try
        {
            mappedRequest = _requestMapper.Map(request.FiscalContext);
        }
        catch (ArgumentException)
        {
            return Blocked(request, "original_fiscal_request_facts_unavailable", attemptedAt);
        }

        if (!string.Equals(
                mappedRequest.UpstreamFinalityRef,
                summary.UpstreamFinalityReference,
                StringComparison.Ordinal) ||
            !string.Equals(
                mappedRequest.PayableBasis.UpstreamFinalityRef,
                summary.UpstreamFinalityReference,
                StringComparison.Ordinal))
        {
            return Blocked(request, "idempotency_context_mismatch", attemptedAt);
        }

        if (summary.SitePosServerId is not null &&
            mappedRequest.SitePosServerId != summary.SitePosServerId)
        {
            return Blocked(request, "site_pos_server_context_mismatch", attemptedAt);
        }

        var calculatedHash = _semanticRequestHashCalculator.Calculate(mappedRequest);
        if (calculatedHash.Status != FiscalSemanticRequestHashSourceStatus.Available ||
            !string.Equals(calculatedHash.HashValue, summary.SemanticRequestHashValue, StringComparison.Ordinal) ||
            !string.Equals(
                calculatedHash.HashAlgorithm,
                summary.SemanticRequestHashAlgorithm,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                calculatedHash.HashSourceVersion,
                summary.SemanticRequestHashSourceVersion,
                StringComparison.Ordinal))
        {
            return Blocked(request, "semantic_request_hash_mismatch", attemptedAt);
        }

        if (HasUnsafeQueueState(summary))
        {
            return Blocked(request, "fiscal_exception_state_not_safe_for_retry_execution", attemptedAt);
        }

        return null;
    }

    private async Task<FiscalExceptionControlledRetryExecutionResult> RecordAndAttachAuditAsync(
        FiscalExceptionControlledRetryExecutionRequest request,
        FiscalExceptionControlledRetryExecutionResult result,
        PosServerFiscalDocumentCreateResult? posServerResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await _auditRepository!.RecordAsync(
                ToAuditWrite(request, result, posServerResult),
                cancellationToken);

            return result with { RetryExecutionAttemptId = record.RetryExecutionAttemptId };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("controlled_retry_execution_audit_persistence_failed", ex);
        }
    }

    private static FiscalExceptionControlledRetryExecutionAttemptWrite ToAuditWrite(
        FiscalExceptionControlledRetryExecutionRequest request,
        FiscalExceptionControlledRetryExecutionResult result,
        PosServerFiscalDocumentCreateResult? posServerResult)
    {
        var summary = request.Detail.Summary;

        return new FiscalExceptionControlledRetryExecutionAttemptWrite(
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            RetryCommandPreparationAttemptId: request.CommandPreparation.RetryCommandPreparationAttemptId,
            RetrySchedulePreparationAttemptId: request.SchedulingPreparation.RetrySchedulePreparationAttemptId,
            ReadbackClassificationBasis: summary.ReadbackClassification,
            SemanticRequestHashValue: summary.SemanticRequestHashValue,
            SemanticRequestHashAlgorithm: summary.SemanticRequestHashAlgorithm,
            SemanticRequestHashSourceVersion: summary.SemanticRequestHashSourceVersion,
            UpstreamFinalityReference: summary.UpstreamFinalityReference,
            ExecutionStatus: result.Status,
            BlockReasonCode: result.BlockReasonCode,
            PosServerOutcome: posServerResult?.Outcome,
            PosServerResultClassification: posServerResult?.ResultClassification,
            PosServerFiscalDocumentId: posServerResult?.FiscalDocumentId,
            FiscalDocumentNumber: posServerResult?.FiscalDocumentNumber,
            FiscalIdentityId: posServerResult?.FiscalIdentityId,
            FiscalSequencePolicyId: posServerResult?.FiscalSequencePolicyId,
            FiscalSequenceValue: posServerResult?.FiscalSequenceValue,
            FiscalSeries: posServerResult?.FiscalSeries,
            FiscalNumberPrefixText: posServerResult?.FiscalNumberPrefixText,
            FiscalNumberSuffixText: posServerResult?.FiscalNumberSuffixText,
            FiscalNumberAssignedAt: posServerResult?.FiscalNumberAssignedAt,
            FiscalNumberAssignedByRef: posServerResult?.FiscalNumberAssignedByRef,
            AttemptedAt: result.AttemptedAt,
            CompletedAt: result.CompletedAt,
            ServiceIdentityId: request.ServiceIdentityId,
            CorrelationId: request.CorrelationId ?? request.Detail.CorrelationId,
            SafeSummary: result.SafeSummary);
    }

    private static FiscalExceptionControlledRetryExecutionResult Blocked(
        FiscalExceptionControlledRetryExecutionRequest request,
        string blockReasonCode,
        DateTimeOffset attemptedAt,
        string? safeSummary = null) =>
        Result(
            request,
            FiscalExceptionControlledRetryExecutionStatus.Blocked,
            blockReasonCode,
            safeSummary ?? $"controlled_retry_execution_blocked:{blockReasonCode}",
            attemptedAt,
            completedAt: null,
            retryAttemptId: null,
            posServerResult: null,
            posServerPostCalled: false,
            fiscalReferenceSuccessRecorded: false);

    private static FiscalExceptionControlledRetryExecutionResult Result(
        FiscalExceptionControlledRetryExecutionRequest request,
        FiscalExceptionControlledRetryExecutionStatus status,
        string? blockReasonCode,
        string safeSummary,
        DateTimeOffset attemptedAt,
        DateTimeOffset? completedAt,
        Guid? retryAttemptId,
        PosServerFiscalDocumentCreateResult? posServerResult,
        bool posServerPostCalled,
        bool fiscalReferenceSuccessRecorded)
    {
        var summary = request.Detail.Summary;

        return new FiscalExceptionControlledRetryExecutionResult(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            RetryExecutionAttemptId: retryAttemptId,
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            RetryCommandPreparationAttemptId: request.CommandPreparation.RetryCommandPreparationAttemptId,
            RetrySchedulePreparationAttemptId: request.SchedulingPreparation.RetrySchedulePreparationAttemptId,
            ReadbackClassificationBasis: summary.ReadbackClassification,
            SemanticRequestHashValue: summary.SemanticRequestHashValue,
            SemanticRequestHashAlgorithm: summary.SemanticRequestHashAlgorithm,
            SemanticRequestHashSourceVersion: summary.SemanticRequestHashSourceVersion,
            UpstreamFinalityReference: summary.UpstreamFinalityReference,
            PosServerOutcome: posServerResult?.Outcome,
            PosServerResultClassification: posServerResult?.ResultClassification,
            PosServerFiscalDocumentId: posServerResult?.FiscalDocumentId,
            FiscalDocumentNumber: posServerResult?.FiscalDocumentNumber,
            AttemptedAt: attemptedAt,
            CompletedAt: completedAt,
            PosServerPostCalled: posServerPostCalled,
            RetryExecuted: posServerPostCalled,
            RetryExecutionAvailable: false,
            BatchExecutionPathAvailable: false,
            PublicEndpointExposed: false,
            ExecutableJobEnqueued: false,
            PaymentFinalityChanged: false,
            FiscalReferenceSuccessRecorded: fiscalReferenceSuccessRecorded,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);
    }

    private static FiscalExceptionControlledRetryExecutionStatus ResolveExecutionStatus(
        FiscalIssuancePosServerLiveIntegrationResult liveResult)
    {
        if (liveResult.Status == FiscalIssuancePosServerLiveIntegrationStatus.Disabled ||
            liveResult.Status == FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid)
        {
            return FiscalExceptionControlledRetryExecutionStatus.Unavailable;
        }

        if (liveResult.Status == FiscalIssuancePosServerLiveIntegrationStatus.LocalContextInvalid)
        {
            return FiscalExceptionControlledRetryExecutionStatus.Blocked;
        }

        if (liveResult.PosServerResult is null)
        {
            return FiscalExceptionControlledRetryExecutionStatus.Unknown;
        }

        if (liveResult.PosServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted &&
            liveResult.PosServerResult.ResultClassification == FiscalIssuanceResultClassification.IdempotentReplay)
        {
            return FiscalExceptionControlledRetryExecutionStatus.ReplayMatched;
        }

        if (liveResult.PosServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted &&
            liveResult.PosServerResult.ResultClassification == FiscalIssuanceResultClassification.NewlyCreated)
        {
            return FiscalExceptionControlledRetryExecutionStatus.Executed;
        }

        return liveResult.PosServerResult.Outcome switch
        {
            PosServerFiscalDocumentOutcome.Conflict => FiscalExceptionControlledRetryExecutionStatus.Conflict,
            PosServerFiscalDocumentOutcome.FailedService or
                PosServerFiscalDocumentOutcome.InvalidResponse => FiscalExceptionControlledRetryExecutionStatus.Unknown,
            _ => FiscalExceptionControlledRetryExecutionStatus.Failed
        };
    }

    private static string? ToBlockReason(FiscalIssuancePosServerLiveIntegrationResult liveResult)
    {
        if (liveResult.PosServerResult?.Outcome == PosServerFiscalDocumentOutcome.Conflict)
        {
            return "pos_server_idempotency_conflict";
        }

        return liveResult.Status switch
        {
            FiscalIssuancePosServerLiveIntegrationStatus.Disabled =>
                "pos_server_fiscal_issuance_live_call_disabled",
            FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid =>
                "pos_server_fiscal_issuance_live_call_configuration_invalid",
            FiscalIssuancePosServerLiveIntegrationStatus.LocalContextInvalid =>
                liveResult.Errors.FirstOrDefault() ?? "pos_server_fiscal_request_context_invalid",
            _ when liveResult.PosServerResult is { Succeeded: false } =>
                liveResult.PosServerResult.Code,
            _ => null
        };
    }

    private static string ToSafeSummary(
        FiscalExceptionControlledRetryExecutionStatus status,
        FiscalIssuancePosServerLiveIntegrationResult liveResult) =>
        status switch
        {
            FiscalExceptionControlledRetryExecutionStatus.Executed =>
                "controlled_retry_execution_applied_newly_created_pos_server_evidence",
            FiscalExceptionControlledRetryExecutionStatus.ReplayMatched =>
                "controlled_retry_execution_applied_idempotent_replay_pos_server_evidence",
            FiscalExceptionControlledRetryExecutionStatus.Conflict =>
                "controlled_retry_execution_blocked_pos_server_idempotency_conflict_no_loop",
            FiscalExceptionControlledRetryExecutionStatus.Unavailable =>
                "controlled_retry_execution_unavailable_pos_server_path_not_ready",
            FiscalExceptionControlledRetryExecutionStatus.Blocked =>
                "controlled_retry_execution_blocked_local_context_invalid",
            FiscalExceptionControlledRetryExecutionStatus.Unknown =>
                "controlled_retry_execution_unknown_requires_readback_before_future_retry",
            _ when liveResult.PosServerResult is { Succeeded: false } =>
                "controlled_retry_execution_failed_no_automatic_loop",
            _ => "controlled_retry_execution_completed"
        };

    private static bool IsNewUpstreamFinalityReferenceRequested(
        string originalUpstreamFinalityReference,
        string? requestedUpstreamFinalityReference) =>
        !string.IsNullOrWhiteSpace(requestedUpstreamFinalityReference) &&
        !string.Equals(
            originalUpstreamFinalityReference.Trim(),
            requestedUpstreamFinalityReference.Trim(),
            StringComparison.Ordinal);

    private static bool HasUnsafeQueueState(FiscalExceptionQueueCaseSummary summary) =>
        summary.QueueState is FiscalExceptionQueueState.ManualReviewRequired
            or FiscalExceptionQueueState.MismatchReview
            or FiscalExceptionQueueState.Reconciled
            or FiscalExceptionQueueState.Closed ||
        summary.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceManualReview
            or FiscalIssuanceIntegrationState.FiscalIssuanceConflict
            or FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled;

    private static string ToReadbackBlockReason(FiscalExceptionReadbackClassification? classification) =>
        classification switch
        {
            null => "readback_attempt_history_missing",
            FiscalExceptionReadbackClassification.Matched => "readback_matched",
            FiscalExceptionReadbackClassification.Mismatch => "readback_mismatch",
            FiscalExceptionReadbackClassification.Failed => "readback_failed",
            FiscalExceptionReadbackClassification.Unavailable => "readback_unavailable",
            FiscalExceptionReadbackClassification.Unknown => "readback_unknown",
            FiscalExceptionReadbackClassification.IdentifierMissing => "readback_identifier_missing",
            FiscalExceptionReadbackClassification.NotSupportedYet => "readback_not_supported_yet",
            _ => "readback_classification_unsafe"
        };
}
