using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionRetryEligibilityEvaluator : IFiscalExceptionRetryEligibilityEvaluator
{
    public FiscalExceptionRetryEligibilityEvaluation Evaluate(FiscalExceptionQueueCaseDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var evaluatedAt = DateTimeOffset.UtcNow;
        var summary = detail.Summary;
        var readbackClassification = summary.ReadbackClassification;

        if (summary.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceReconciled ||
            summary.QueueState == FiscalExceptionQueueState.Reconciled ||
            summary.QueueState == FiscalExceptionQueueState.Closed)
        {
            return NotRequired(
                FiscalExceptionRetryEligibilityStatus.NotRequiredRecorded,
                "not_required_recorded",
                "retry_not_required_recorded_or_reconciled",
                evaluatedAt,
                detail);
        }

        if (summary.QueueState is FiscalExceptionQueueState.ManualReviewRequired
            or FiscalExceptionQueueState.MismatchReview)
        {
            return Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedManualReview,
                "manual_review_required",
                "retry_blocked_manual_review_required",
                evaluatedAt,
                detail);
        }

        if (summary.QueueState == FiscalExceptionQueueState.BlockedRequiresConfigFix ||
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
                or FiscalIssuanceExceptionReason.FiscalDocumentNumberFormatFailed)
        {
            return Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedConfiguration,
                "fiscal_configuration_invalid_or_missing",
                "retry_blocked_fiscal_configuration_invalid_or_missing",
                evaluatedAt,
                detail);
        }

        if (summary.ReadbackAttemptCount is null or < 1 || readbackClassification is null)
        {
            return Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback,
                "readback_attempt_history_missing",
                "retry_blocked_readback_attempt_history_missing",
                evaluatedAt,
                detail);
        }

        var readbackGate = EvaluateReadbackGate(readbackClassification.Value, evaluatedAt, detail);
        if (readbackGate is not null)
        {
            return readbackGate;
        }

        if (!HasOriginalRequestContext(detail))
        {
            return Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedMissingRequestContext,
                "original_request_context_missing",
                "retry_blocked_original_request_context_missing",
                evaluatedAt,
                detail);
        }

        if (string.IsNullOrWhiteSpace(summary.UpstreamFinalityReference))
        {
            return Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedMissingUpstreamFinalityReference,
                "upstream_finality_reference_missing",
                "retry_blocked_upstream_finality_reference_missing",
                evaluatedAt,
                detail);
        }

        var semanticHashReadiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(summary);
        if (!FiscalExceptionSemanticHashReadinessPolicy.IsReady(semanticHashReadiness.Status))
        {
            return Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedSemanticHashNotReady,
                semanticHashReadiness.BlockReasonCode ?? "semantic_hash_not_ready",
                semanticHashReadiness.SafeSummary,
                evaluatedAt,
                detail);
        }

        return new FiscalExceptionRetryEligibilityEvaluation(
            Status: FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning,
            Decision: FiscalExceptionRetryEligibilityDecision.Eligible,
            BlockReasonCode: null,
            SafeSummary: "retry_eligible_for_controlled_retry_planning_no_execution",
            EvaluatedAt: evaluatedAt,
            BasedOnReadbackClassification: readbackClassification,
            LastReadbackAttemptAt: summary.LastReadbackAttemptAt,
            ReadbackAttemptCount: summary.ReadbackAttemptCount,
            RetryExecutionAvailable: false);
    }

    private static FiscalExceptionRetryEligibilityEvaluation? EvaluateReadbackGate(
        FiscalExceptionReadbackClassification readbackClassification,
        DateTimeOffset evaluatedAt,
        FiscalExceptionQueueCaseDetail detail) =>
        readbackClassification switch
        {
            FiscalExceptionReadbackClassification.NotFound => null,
            FiscalExceptionReadbackClassification.Matched => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackMatched,
                "readback_matched",
                "retry_blocked_readback_matched_record_or_reconcile",
                evaluatedAt,
                detail),
            FiscalExceptionReadbackClassification.Mismatch => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackMismatch,
                "readback_mismatch",
                "retry_blocked_readback_mismatch_manual_review_required",
                evaluatedAt,
                detail),
            FiscalExceptionReadbackClassification.Failed => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed,
                "readback_failed",
                "retry_blocked_readback_failed",
                evaluatedAt,
                detail),
            FiscalExceptionReadbackClassification.Unavailable => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed,
                "readback_unavailable",
                "retry_blocked_readback_unavailable",
                evaluatedAt,
                detail),
            FiscalExceptionReadbackClassification.Unknown => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed,
                "readback_unknown",
                "retry_blocked_readback_unknown",
                evaluatedAt,
                detail),
            FiscalExceptionReadbackClassification.IdentifierMissing => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedIdentifierMissing,
                "readback_identifier_missing",
                "retry_blocked_readback_identifier_missing",
                evaluatedAt,
                detail),
            FiscalExceptionReadbackClassification.NotSupportedYet => Unavailable(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackUnsupported,
                "readback_not_supported_yet",
                "retry_unavailable_readback_not_supported_yet",
                evaluatedAt,
                detail),
            _ => Blocked(
                FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed,
                "readback_classification_unsafe",
                "retry_blocked_readback_classification_unsafe",
                evaluatedAt,
                detail)
        };

    private static bool HasOriginalRequestContext(FiscalExceptionQueueCaseDetail detail)
    {
        var summary = detail.Summary;
        return summary.FiscalIssuanceReferenceId != Guid.Empty &&
            summary.PaymentConfirmationId != Guid.Empty &&
            summary.PaymentAttemptId != Guid.Empty &&
            summary.ParkingSessionId != Guid.Empty &&
            (summary.SitePosServerId is not null && summary.SitePosServerId != Guid.Empty ||
                !string.IsNullOrWhiteSpace(summary.SitePosServerRef));
    }

    private static FiscalExceptionRetryEligibilityEvaluation Blocked(
        FiscalExceptionRetryEligibilityStatus status,
        string blockReasonCode,
        string safeSummary,
        DateTimeOffset evaluatedAt,
        FiscalExceptionQueueCaseDetail detail) =>
        new(
            Status: status,
            Decision: FiscalExceptionRetryEligibilityDecision.Blocked,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            EvaluatedAt: evaluatedAt,
            BasedOnReadbackClassification: detail.Summary.ReadbackClassification,
            LastReadbackAttemptAt: detail.Summary.LastReadbackAttemptAt,
            ReadbackAttemptCount: detail.Summary.ReadbackAttemptCount,
            RetryExecutionAvailable: false);

    private static FiscalExceptionRetryEligibilityEvaluation Unavailable(
        FiscalExceptionRetryEligibilityStatus status,
        string blockReasonCode,
        string safeSummary,
        DateTimeOffset evaluatedAt,
        FiscalExceptionQueueCaseDetail detail) =>
        new(
            Status: status,
            Decision: FiscalExceptionRetryEligibilityDecision.Unavailable,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            EvaluatedAt: evaluatedAt,
            BasedOnReadbackClassification: detail.Summary.ReadbackClassification,
            LastReadbackAttemptAt: detail.Summary.LastReadbackAttemptAt,
            ReadbackAttemptCount: detail.Summary.ReadbackAttemptCount,
            RetryExecutionAvailable: false);

    private static FiscalExceptionRetryEligibilityEvaluation NotRequired(
        FiscalExceptionRetryEligibilityStatus status,
        string blockReasonCode,
        string safeSummary,
        DateTimeOffset evaluatedAt,
        FiscalExceptionQueueCaseDetail detail) =>
        new(
            Status: status,
            Decision: FiscalExceptionRetryEligibilityDecision.NotRequired,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            EvaluatedAt: evaluatedAt,
            BasedOnReadbackClassification: detail.Summary.ReadbackClassification,
            LastReadbackAttemptAt: detail.Summary.LastReadbackAttemptAt,
            ReadbackAttemptCount: detail.Summary.ReadbackAttemptCount,
            RetryExecutionAvailable: false);
}
