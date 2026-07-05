using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionRetryCommandPreparationService : IFiscalExceptionRetryCommandPreparationService
{
    public FiscalExceptionRetryCommandPreparationResult Prepare(
        FiscalExceptionRetryCommandPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);

        var detail = request.Detail;
        var summary = detail.Summary;

        var idempotencyStatus = ResolveIdempotencyStatus(
            summary.UpstreamFinalityReference,
            request.RequestedUpstreamFinalityReference);

        if (request.TreatAsExecutableCommand && !summary.RetryExecutionAvailable)
        {
            return Blocked(
                "retry_execution_not_available",
                "retry_command_blocked_execution_not_available",
                detail,
                idempotencyStatus);
        }

        if (idempotencyStatus == FiscalExceptionIdempotencyContextAvailabilityStatus.NewUpstreamFinalityReferenceRejected)
        {
            return Blocked(
                "new_upstream_finality_reference_rejected",
                "retry_command_blocked_new_upstream_finality_reference_rejected",
                detail,
                idempotencyStatus);
        }

        if (summary.RetryEligibilityDecision != FiscalExceptionRetryEligibilityDecision.Eligible ||
            summary.RetryEligibilityStatus != FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning)
        {
            return Blocked(
                summary.RetryBlockReasonCode ?? "retry_eligibility_not_eligible",
                "retry_command_blocked_retry_eligibility_not_eligible",
                detail,
                idempotencyStatus);
        }

        if (summary.ReadbackAttemptCount is null or < 1)
        {
            return Blocked(
                "readback_attempt_history_missing",
                "retry_command_blocked_readback_attempt_history_missing",
                detail,
                idempotencyStatus);
        }

        if (summary.ReadbackClassification != FiscalExceptionReadbackClassification.NotFound)
        {
            return Blocked(
                ToReadbackBlockReason(summary.ReadbackClassification),
                "retry_command_blocked_latest_readback_not_not_found",
                detail,
                idempotencyStatus);
        }

        if (MissingRequestContext(summary))
        {
            return Blocked(
                "original_request_context_missing",
                "retry_command_blocked_original_request_context_missing",
                detail,
                idempotencyStatus);
        }

        if (summary.SitePosServerId is null && string.IsNullOrWhiteSpace(summary.SitePosServerRef))
        {
            return Blocked(
                "site_pos_server_context_missing",
                "retry_command_blocked_site_pos_server_context_missing",
                detail,
                idempotencyStatus);
        }

        if (idempotencyStatus == FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference)
        {
            return Blocked(
                "upstream_finality_reference_missing",
                "retry_command_blocked_upstream_finality_reference_missing",
                detail,
                idempotencyStatus);
        }

        if (HasUnsafeQueueState(summary) || HasConfigurationFailure(summary))
        {
            return Blocked(
                "fiscal_exception_state_not_safe_for_retry_command",
                "retry_command_blocked_fiscal_exception_state_not_safe",
                detail,
                idempotencyStatus);
        }

        if (summary.SemanticRequestHashAvailabilityStatus !=
            FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed)
        {
            return Unavailable(
                "semantic_request_hash_required_but_missing",
                "retry_command_unavailable_semantic_request_hash_required_but_missing",
                detail,
                idempotencyStatus);
        }

        return Result(
            FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable,
            blockReasonCode: null,
            safeSummary: "retry_command_prepared_non_executable",
            detail,
            idempotencyStatus,
            command: new FiscalExceptionRetryCommandEnvelope(
                FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
                PaymentConfirmationId: summary.PaymentConfirmationId,
                PaymentAttemptId: summary.PaymentAttemptId,
                ParkingSessionId: summary.ParkingSessionId,
                SiteId: summary.SiteId,
                SitePosServerId: summary.SitePosServerId,
                SitePosServerRef: summary.SitePosServerRef,
                FiscalDocumentTypeContextStatus: "not_available_in_current_fiscal_reference_model",
                UpstreamFinalityReference: summary.UpstreamFinalityReference,
                SemanticRequestHashAvailabilityStatus: summary.SemanticRequestHashAvailabilityStatus,
                LatestReadbackClassificationBasis: summary.ReadbackClassification!.Value,
                RetryEligibilityDecisionBasis: summary.RetryEligibilityDecision,
                SafeBlockReasonCode: null,
                CorrelationId: detail.CorrelationId,
                Executable: false));
    }

    private static FiscalExceptionIdempotencyContextAvailabilityStatus ResolveIdempotencyStatus(
        string? originalUpstreamFinalityReference,
        string? requestedUpstreamFinalityReference)
    {
        if (string.IsNullOrWhiteSpace(originalUpstreamFinalityReference))
        {
            return FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference;
        }

        if (!string.IsNullOrWhiteSpace(requestedUpstreamFinalityReference) &&
            !string.Equals(
                originalUpstreamFinalityReference.Trim(),
                requestedUpstreamFinalityReference.Trim(),
                StringComparison.Ordinal))
        {
            return FiscalExceptionIdempotencyContextAvailabilityStatus.NewUpstreamFinalityReferenceRejected;
        }

        return FiscalExceptionIdempotencyContextAvailabilityStatus.Available;
    }

    private static bool MissingRequestContext(FiscalExceptionQueueCaseSummary summary) =>
        summary.FiscalIssuanceReferenceId == Guid.Empty ||
        summary.PaymentConfirmationId == Guid.Empty ||
        summary.PaymentAttemptId == Guid.Empty ||
        summary.ParkingSessionId == Guid.Empty;

    private static bool HasUnsafeQueueState(FiscalExceptionQueueCaseSummary summary) =>
        summary.QueueState is FiscalExceptionQueueState.ManualReviewRequired
            or FiscalExceptionQueueState.MismatchReview
            or FiscalExceptionQueueState.Reconciled
            or FiscalExceptionQueueState.Closed ||
        summary.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceManualReview
            or FiscalIssuanceIntegrationState.FiscalIssuanceConflict
            or FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled;

    private static bool HasConfigurationFailure(FiscalExceptionQueueCaseSummary summary) =>
        summary.QueueState == FiscalExceptionQueueState.BlockedRequiresConfigFix ||
        summary.Category == FiscalExceptionQueueCategory.FiscalConfigurationMissing ||
        summary.LatestExceptionReason is FiscalIssuanceExceptionReason.FiscalIdentityNotFound
            or FiscalIssuanceExceptionReason.FiscalIdentityAmbiguous
            or FiscalIssuanceExceptionReason.FiscalIdentityNotEffective
            or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound
            or FiscalIssuanceExceptionReason.FiscalSequencePolicyAmbiguous
            or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotEffective
            or FiscalIssuanceExceptionReason.FiscalSequenceStateNotFound
            or FiscalIssuanceExceptionReason.FiscalSequenceStateNotEffective
            or FiscalIssuanceExceptionReason.FiscalNumberAllocationFailed
            or FiscalIssuanceExceptionReason.FiscalDocumentNumberFormatFailed;

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

    private static FiscalExceptionRetryCommandPreparationResult Blocked(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionIdempotencyContextAvailabilityStatus idempotencyStatus) =>
        Result(
            FiscalExceptionRetryCommandPreparationStatus.Blocked,
            blockReasonCode,
            safeSummary,
            detail,
            idempotencyStatus,
            command: null);

    private static FiscalExceptionRetryCommandPreparationResult Unavailable(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionIdempotencyContextAvailabilityStatus idempotencyStatus) =>
        Result(
            FiscalExceptionRetryCommandPreparationStatus.Unavailable,
            blockReasonCode,
            safeSummary,
            detail,
            idempotencyStatus,
            command: null);

    private static FiscalExceptionRetryCommandPreparationResult Result(
        FiscalExceptionRetryCommandPreparationStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionIdempotencyContextAvailabilityStatus idempotencyStatus,
        FiscalExceptionRetryCommandEnvelope? command) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            Command: command,
            SemanticRequestHashAvailabilityStatus: command is null
                ? FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing
                : command.SemanticRequestHashAvailabilityStatus,
            IdempotencyContextAvailabilityStatus: idempotencyStatus,
            PosServerPostCalled: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);
}
