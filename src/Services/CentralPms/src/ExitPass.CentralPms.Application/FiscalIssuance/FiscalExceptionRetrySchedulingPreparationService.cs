using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionRetrySchedulingPreparationService :
    IFiscalExceptionRetrySchedulingPreparationService
{
    private readonly FiscalExceptionRetrySchedulingPreparationOptions _options;
    private readonly IFiscalExceptionRetrySchedulingPreparationAuditRepository? _auditRepository;

    public FiscalExceptionRetrySchedulingPreparationService()
        : this(new FiscalExceptionRetrySchedulingPreparationOptions(), null)
    {
    }

    public FiscalExceptionRetrySchedulingPreparationService(
        FiscalExceptionRetrySchedulingPreparationOptions options)
        : this(options, null)
    {
    }

    public FiscalExceptionRetrySchedulingPreparationService(
        FiscalExceptionRetrySchedulingPreparationOptions options,
        IFiscalExceptionRetrySchedulingPreparationAuditRepository? auditRepository)
    {
        _options = options;
        _auditRepository = auditRepository;
    }

    public async Task<FiscalExceptionRetrySchedulingPreparationResult> PrepareAsync(
        FiscalExceptionRetrySchedulingPreparationRequest request,
        CancellationToken cancellationToken)
    {
        var evaluated = Evaluate(request);
        if (evaluated.Status != FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared)
        {
            return evaluated;
        }

        if (_auditRepository is null)
        {
            return Unavailable(
                "retry_scheduling_audit_persistence_unavailable",
                "retry_scheduling_unavailable_audit_persistence_unavailable");
        }

        try
        {
            var record = await _auditRepository.RecordAsync(
                ToAuditWrite(request, evaluated),
                cancellationToken);

            return evaluated with
            {
                Schedule = evaluated.Schedule is null
                    ? null
                    : evaluated.Schedule with
                    {
                        RetrySchedulePreparationAttemptId = record.RetrySchedulePreparationAttemptId
                    },
                RetrySchedulePreparationAttemptId = record.RetrySchedulePreparationAttemptId,
                RetrySchedulePreparationAttemptedAt = record.RequestedAt
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "retry_scheduling_preparation_audit_persistence_failed",
                ex);
        }
    }

    private FiscalExceptionRetrySchedulingPreparationResult Evaluate(
        FiscalExceptionRetrySchedulingPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);
        ArgumentNullException.ThrowIfNull(request.CommandPreparation);

        if (!_options.EnableSchedulePreparation)
        {
            return Disabled(
                "retry_scheduling_preparation_disabled",
                "retry_scheduling_preparation_disabled_by_default");
        }

        var detail = request.Detail;
        var summary = detail.Summary;
        var commandPreparation = request.CommandPreparation;

        if (request.TreatAsExecutableJob)
        {
            return Blocked(
                "retry_execution_not_available",
                "retry_scheduling_blocked_execution_not_available");
        }

        if (IsNewUpstreamFinalityReferenceRequested(
            summary.UpstreamFinalityReference,
            request.RequestedUpstreamFinalityReference))
        {
            return Blocked(
                "new_upstream_finality_reference_rejected",
                "retry_scheduling_blocked_new_upstream_finality_reference_rejected");
        }

        if (commandPreparation.Status != FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable ||
            commandPreparation.Command is null ||
            commandPreparation.Command.Executable)
        {
            return Blocked(
                commandPreparation.BlockReasonCode ?? "retry_command_preparation_not_safe",
                "retry_scheduling_blocked_retry_command_preparation_not_safe");
        }

        if (commandPreparation.RetryCommandPreparationAttemptId is null)
        {
            return Blocked(
                "retry_command_preparation_audit_missing",
                "retry_scheduling_blocked_command_preparation_audit_missing");
        }

        if (summary.RetryEligibilityDecision != FiscalExceptionRetryEligibilityDecision.Eligible ||
            summary.RetryEligibilityStatus != FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning)
        {
            return Blocked(
                summary.RetryBlockReasonCode ?? "retry_eligibility_not_eligible",
                "retry_scheduling_blocked_retry_eligibility_not_eligible");
        }

        if (summary.ReadbackAttemptCount is null or < 1 ||
            summary.ReadbackClassification != FiscalExceptionReadbackClassification.NotFound)
        {
            return Blocked(
                ToReadbackBlockReason(summary.ReadbackClassification),
                "retry_scheduling_blocked_readback_basis_not_not_found");
        }

        if (summary.SemanticRequestHashAvailabilityStatus !=
                FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashValue) ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashAlgorithm) ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashSourceVersion))
        {
            return Blocked(
                "semantic_request_hash_required_but_missing",
                "retry_scheduling_blocked_semantic_request_hash_missing_or_unconfirmed");
        }

        var semanticHashReadiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(summary);
        if (!FiscalExceptionSemanticHashReadinessPolicy.IsReady(semanticHashReadiness.Status))
        {
            return Blocked(
                semanticHashReadiness.BlockReasonCode ?? "semantic_hash_not_ready",
                semanticHashReadiness.SafeSummary);
        }

        if (summary.IdempotencyContextAvailabilityStatus !=
            FiscalExceptionIdempotencyContextAvailabilityStatus.Available)
        {
            return Blocked(
                "idempotency_context_not_available",
                "retry_scheduling_blocked_idempotency_context_not_available");
        }

        if (HasUnsafeQueueState(summary))
        {
            return Blocked(
                "fiscal_exception_state_not_safe_for_retry_scheduling",
                "retry_scheduling_blocked_fiscal_exception_state_not_safe");
        }

        if (!_options.RetrySchedulePolicyConfigured)
        {
            return Unavailable(
                "retry_schedule_policy_not_configured",
                "retry_scheduling_unavailable_policy_not_configured");
        }

        if (!_options.RetryBackoffPolicyConfigured)
        {
            return Unavailable(
                "retry_backoff_policy_not_configured",
                "retry_scheduling_unavailable_backoff_policy_not_configured");
        }

        var requestedAt = DateTimeOffset.UtcNow;
        return Result(
            FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared,
            blockReasonCode: null,
            safeSummary: "retry_scheduling_prepared_non_executable",
            schedule: new FiscalExceptionRetrySchedulePreparationEnvelope(
                RetrySchedulePreparationAttemptId: Guid.Empty,
                FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
                RetryCommandPreparationAttemptId: commandPreparation.RetryCommandPreparationAttemptId,
                RetryEligibilityDecisionBasis: summary.RetryEligibilityDecision,
                LatestReadbackClassificationBasis: summary.ReadbackClassification,
                SemanticRequestHashAvailabilityStatus: summary.SemanticRequestHashAvailabilityStatus,
                IdempotencyContextAvailabilityStatus: summary.IdempotencyContextAvailabilityStatus,
                UpstreamFinalityReference: summary.UpstreamFinalityReference,
                RequestedAt: requestedAt,
                EarliestEligibleAt: requestedAt,
                SchedulePolicySummary: "retry_schedule_policy_configured_backoff_configured_no_execution",
                CorrelationId: detail.CorrelationId,
                Executable: false));
    }

    private static FiscalExceptionRetrySchedulingPreparationAttemptWrite ToAuditWrite(
        FiscalExceptionRetrySchedulingPreparationRequest request,
        FiscalExceptionRetrySchedulingPreparationResult result)
    {
        var summary = request.Detail.Summary;

        return new FiscalExceptionRetrySchedulingPreparationAttemptWrite(
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            RetryCommandPreparationAttemptId: request.CommandPreparation.RetryCommandPreparationAttemptId,
            PaymentConfirmationId: NullIfEmpty(summary.PaymentConfirmationId),
            PaymentAttemptId: NullIfEmpty(summary.PaymentAttemptId),
            ParkingSessionId: NullIfEmpty(summary.ParkingSessionId),
            SiteId: NullIfEmpty(summary.SiteId),
            SitePosServerId: NullIfEmpty(summary.SitePosServerId),
            SitePosServerRef: summary.SitePosServerRef,
            LatestReadbackClassificationBasis: summary.ReadbackClassification,
            RetryEligibilityDecisionBasis: summary.RetryEligibilityDecision,
            SemanticRequestHashAvailabilityStatus: summary.SemanticRequestHashAvailabilityStatus,
            IdempotencyContextAvailabilityStatus: summary.IdempotencyContextAvailabilityStatus,
            SchedulingPreparationStatus: result.Status,
            SchedulingBlockReasonCode: result.BlockReasonCode,
            RequestedAt: result.Schedule?.RequestedAt ?? DateTimeOffset.UtcNow,
            EarliestEligibleAt: result.Schedule?.EarliestEligibleAt,
            SafeSummary: result.SafeSummary,
            CorrelationId: request.Detail.CorrelationId,
            ServiceIdentityId: request.ServiceIdentityId);
    }

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
            FiscalExceptionReadbackClassification.NotFound => "readback_not_found",
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

    private static FiscalExceptionRetrySchedulingPreparationResult Disabled(
        string blockReasonCode,
        string safeSummary) =>
        Result(
            FiscalExceptionRetrySchedulingPreparationStatus.Disabled,
            blockReasonCode,
            safeSummary,
            schedule: null);

    private static FiscalExceptionRetrySchedulingPreparationResult Blocked(
        string blockReasonCode,
        string safeSummary) =>
        Result(
            FiscalExceptionRetrySchedulingPreparationStatus.Blocked,
            blockReasonCode,
            safeSummary,
            schedule: null);

    private static FiscalExceptionRetrySchedulingPreparationResult Unavailable(
        string blockReasonCode,
        string safeSummary) =>
        Result(
            FiscalExceptionRetrySchedulingPreparationStatus.Unavailable,
            blockReasonCode,
            safeSummary,
            schedule: null);

    private static FiscalExceptionRetrySchedulingPreparationResult Result(
        FiscalExceptionRetrySchedulingPreparationStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionRetrySchedulePreparationEnvelope? schedule) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            Schedule: schedule,
            PosServerPostCalled: false,
            ExecutableJobEnqueued: false,
            RetryEndpointExposed: false,
            PaymentFinalityChanged: false,
            FiscalReferenceSuccessRecorded: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false,
            RetrySchedulePreparationAttemptId: null,
            RetrySchedulePreparationAttemptedAt: null);

    private static Guid? NullIfEmpty(Guid value) =>
        value == Guid.Empty ? null : value;

    private static Guid? NullIfEmpty(Guid? value) =>
        value is null || value == Guid.Empty ? null : value;
}
