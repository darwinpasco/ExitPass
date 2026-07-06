using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionRetryExecutionPreparationService :
    IFiscalExceptionRetryExecutionPreparationService
{
    private readonly FiscalExceptionRetryExecutionPreparationOptions _options;

    public FiscalExceptionRetryExecutionPreparationService()
        : this(new FiscalExceptionRetryExecutionPreparationOptions())
    {
    }

    public FiscalExceptionRetryExecutionPreparationService(
        FiscalExceptionRetryExecutionPreparationOptions options)
    {
        _options = options;
    }

    public Task<FiscalExceptionRetryExecutionPreparationResult> EvaluateAsync(
        FiscalExceptionRetryExecutionPreparationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Evaluate(request));
    }

    private FiscalExceptionRetryExecutionPreparationResult Evaluate(
        FiscalExceptionRetryExecutionPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);
        ArgumentNullException.ThrowIfNull(request.CommandPreparation);
        ArgumentNullException.ThrowIfNull(request.SchedulingPreparation);

        if (!_options.EnableExecutionPreparation)
        {
            return Result(
                FiscalExceptionRetryExecutionPreparationStatus.Disabled,
                "retry_execution_preparation_disabled",
                "retry_execution_preparation_disabled_by_default",
                FiscalExceptionRetryExecutionAuthorizationStatus.NotEvaluated,
                FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
                dualControlRequired: false);
        }

        var detail = request.Detail;
        var summary = detail.Summary;
        var commandPreparation = request.CommandPreparation;
        var schedulingPreparation = request.SchedulingPreparation;

        if (request.TreatAsExecutableRetry)
        {
            return Blocked(
                "retry_execution_not_available",
                "retry_execution_preparation_blocked_execution_not_available",
                FiscalExceptionRetryExecutionAuthorizationStatus.NotEvaluated,
                FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (request.OperatorOrSupportActionRequested)
        {
            return Blocked(
                "operator_retry_execution_not_allowed_in_current_policy",
                "retry_execution_preparation_blocked_operator_execution_not_allowed",
                FiscalExceptionRetryExecutionAuthorizationStatus.OperatorActionNotAllowed,
                FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (IsNewUpstreamFinalityReferenceRequested(
            summary.UpstreamFinalityReference,
            request.RequestedUpstreamFinalityReference))
        {
            return Blocked(
                "new_upstream_finality_reference_rejected",
                "retry_execution_preparation_blocked_new_upstream_finality_reference_rejected",
                FiscalExceptionRetryExecutionAuthorizationStatus.NotEvaluated,
                FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (!_options.ServiceIdentityAllowed)
        {
            return Unavailable(
                "service_identity_not_authorized_for_retry_execution_prep",
                "retry_execution_preparation_unavailable_service_identity_not_authorized",
                FiscalExceptionRetryExecutionAuthorizationStatus.ServiceIdentityNotAllowed,
                FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (_options.ProductionImpacting && !_options.DualControlSatisfied)
        {
            return Result(
                FiscalExceptionRetryExecutionPreparationStatus.RequiresDualControl,
                "dual_control_required",
                "retry_execution_preparation_requires_dual_control",
                FiscalExceptionRetryExecutionAuthorizationStatus.DualControlRequired,
                FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
                dualControlRequired: true);
        }

        var readiness = ResolvePosServerReadiness();
        if (readiness != FiscalExceptionRetryExecutionPosServerReadinessStatus.Confirmed)
        {
            return Result(
                FiscalExceptionRetryExecutionPreparationStatus.RequiresPosServerReadiness,
                ToPosServerReadinessBlockReason(readiness),
                "retry_execution_preparation_requires_pos_server_readiness_confirmation",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (request.PosServerRetryContractReadiness is not null &&
            request.PosServerRetryContractReadiness.Status !=
                FiscalExceptionPosServerRetryContractReadinessStatus.Ready)
        {
            return Blocked(
                request.PosServerRetryContractReadiness.BlockReasonCode ??
                    "pos_server_retry_contract_readiness_not_ready",
                "retry_execution_preparation_blocked_pos_server_retry_contract_readiness_not_ready",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (schedulingPreparation.Status != FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared ||
            schedulingPreparation.Schedule is null ||
            schedulingPreparation.Schedule.Executable ||
            schedulingPreparation.RetrySchedulePreparationAttemptId is null)
        {
            return Blocked(
                schedulingPreparation.BlockReasonCode ?? "retry_scheduling_preparation_missing",
                "retry_execution_preparation_blocked_retry_scheduling_preparation_missing",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (commandPreparation.Status != FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable ||
            commandPreparation.Command is null ||
            commandPreparation.Command.Executable)
        {
            return Blocked(
                commandPreparation.BlockReasonCode ?? "retry_command_preparation_not_safe",
                "retry_execution_preparation_blocked_retry_command_preparation_not_safe",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (commandPreparation.RetryCommandPreparationAttemptId is null)
        {
            return Blocked(
                "retry_command_preparation_audit_missing",
                "retry_execution_preparation_blocked_command_preparation_audit_missing",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (schedulingPreparation.Schedule.RetryCommandPreparationAttemptId !=
            commandPreparation.RetryCommandPreparationAttemptId)
        {
            return Blocked(
                "retry_command_preparation_audit_mismatch",
                "retry_execution_preparation_blocked_command_preparation_audit_mismatch",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (summary.RetryEligibilityDecision != FiscalExceptionRetryEligibilityDecision.Eligible ||
            summary.RetryEligibilityStatus != FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning)
        {
            return Blocked(
                summary.RetryBlockReasonCode ?? "retry_eligibility_not_eligible",
                "retry_execution_preparation_blocked_retry_eligibility_not_eligible",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (summary.ReadbackAttemptCount is null or < 1 ||
            summary.ReadbackClassification != FiscalExceptionReadbackClassification.NotFound)
        {
            return Blocked(
                ToReadbackBlockReason(summary.ReadbackClassification),
                "retry_execution_preparation_blocked_readback_basis_not_not_found",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (summary.SemanticRequestHashAvailabilityStatus !=
                FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashValue) ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashAlgorithm) ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashSourceVersion))
        {
            return Blocked(
                "semantic_request_hash_required_but_missing",
                "retry_execution_preparation_blocked_semantic_request_hash_missing_or_unconfirmed",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (!string.Equals(
            commandPreparation.Command.SemanticRequestHashValue,
            summary.SemanticRequestHashValue,
            StringComparison.Ordinal))
        {
            return Blocked(
                "semantic_request_hash_mismatch",
                "retry_execution_preparation_blocked_semantic_request_hash_mismatch",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (summary.IdempotencyContextAvailabilityStatus !=
                FiscalExceptionIdempotencyContextAvailabilityStatus.Available ||
            !string.Equals(
                commandPreparation.Command.UpstreamFinalityReference,
                summary.UpstreamFinalityReference,
                StringComparison.Ordinal) ||
            !string.Equals(
                schedulingPreparation.Schedule.UpstreamFinalityReference,
                summary.UpstreamFinalityReference,
                StringComparison.Ordinal))
        {
            return Blocked(
                "idempotency_context_not_available",
                "retry_execution_preparation_blocked_idempotency_context_not_available",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        if (HasUnsafeQueueState(summary))
        {
            return Blocked(
                "fiscal_exception_state_not_safe_for_retry_execution",
                "retry_execution_preparation_blocked_fiscal_exception_state_not_safe",
                AuthorizationStatus(),
                readiness,
                dualControlRequired: _options.ProductionImpacting);
        }

        return Result(
            FiscalExceptionRetryExecutionPreparationStatus.ReadyForExecutionWhenEnabled,
            blockReasonCode: null,
            safeSummary: "retry_execution_preconditions_ready_no_execution",
            AuthorizationStatus(),
            readiness,
            dualControlRequired: _options.ProductionImpacting);
    }

    private FiscalExceptionRetryExecutionAuthorizationStatus AuthorizationStatus() =>
        _options.ProductionImpacting
            ? FiscalExceptionRetryExecutionAuthorizationStatus.DualControlSatisfied
            : FiscalExceptionRetryExecutionAuthorizationStatus.ServiceIdentityAllowed;

    private FiscalExceptionRetryExecutionPosServerReadinessStatus ResolvePosServerReadiness()
    {
        if (!_options.PosServerNumberingReady)
        {
            return FiscalExceptionRetryExecutionPosServerReadinessStatus.NumberingNotReady;
        }

        if (!_options.PosServerIdempotencyContractConfirmed)
        {
            return FiscalExceptionRetryExecutionPosServerReadinessStatus.IdempotencyContractNotConfirmed;
        }

        if (!_options.PosServerSequencePolicyConfirmed)
        {
            return FiscalExceptionRetryExecutionPosServerReadinessStatus.SequencePolicyNotConfirmed;
        }

        if (!_options.PosServerFiscalIdentityConfirmed)
        {
            return FiscalExceptionRetryExecutionPosServerReadinessStatus.FiscalIdentityNotConfirmed;
        }

        return _options.ProductionBirReadinessConfirmed
            ? FiscalExceptionRetryExecutionPosServerReadinessStatus.Confirmed
            : FiscalExceptionRetryExecutionPosServerReadinessStatus.ProductionBirReadinessNotConfirmed;
    }

    private static string ToPosServerReadinessBlockReason(
        FiscalExceptionRetryExecutionPosServerReadinessStatus readiness) =>
        readiness switch
        {
            FiscalExceptionRetryExecutionPosServerReadinessStatus.NumberingNotReady =>
                "pos_server_numbering_not_ready",
            FiscalExceptionRetryExecutionPosServerReadinessStatus.IdempotencyContractNotConfirmed =>
                "pos_server_idempotency_contract_not_confirmed",
            FiscalExceptionRetryExecutionPosServerReadinessStatus.SequencePolicyNotConfirmed =>
                "pos_server_sequence_policy_not_confirmed",
            FiscalExceptionRetryExecutionPosServerReadinessStatus.FiscalIdentityNotConfirmed =>
                "pos_server_fiscal_identity_not_confirmed",
            FiscalExceptionRetryExecutionPosServerReadinessStatus.ProductionBirReadinessNotConfirmed =>
                "production_bir_readiness_not_confirmed",
            _ => "pos_server_readiness_not_confirmed"
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

    private static FiscalExceptionRetryExecutionPreparationResult Blocked(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionRetryExecutionAuthorizationStatus authorizationStatus,
        FiscalExceptionRetryExecutionPosServerReadinessStatus posServerReadinessStatus,
        bool dualControlRequired) =>
        Result(
            FiscalExceptionRetryExecutionPreparationStatus.Blocked,
            blockReasonCode,
            safeSummary,
            authorizationStatus,
            posServerReadinessStatus,
            dualControlRequired);

    private static FiscalExceptionRetryExecutionPreparationResult Unavailable(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionRetryExecutionAuthorizationStatus authorizationStatus,
        FiscalExceptionRetryExecutionPosServerReadinessStatus posServerReadinessStatus,
        bool dualControlRequired) =>
        Result(
            FiscalExceptionRetryExecutionPreparationStatus.Unavailable,
            blockReasonCode,
            safeSummary,
            authorizationStatus,
            posServerReadinessStatus,
            dualControlRequired);

    private static FiscalExceptionRetryExecutionPreparationResult Result(
        FiscalExceptionRetryExecutionPreparationStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionRetryExecutionAuthorizationStatus authorizationStatus,
        FiscalExceptionRetryExecutionPosServerReadinessStatus posServerReadinessStatus,
        bool dualControlRequired) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            AuthorizationStatus: authorizationStatus,
            PosServerReadinessStatus: posServerReadinessStatus,
            DualControlRequired: dualControlRequired,
            PosServerPostCalled: false,
            ExecutableJobEnqueued: false,
            RetryEndpointExposed: false,
            RetryExecuted: false,
            PaymentFinalityChanged: false,
            FiscalReferenceSuccessRecorded: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);
}
