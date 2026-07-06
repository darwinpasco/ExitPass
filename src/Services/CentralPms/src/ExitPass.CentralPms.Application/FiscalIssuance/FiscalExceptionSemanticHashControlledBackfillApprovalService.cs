namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashControlledBackfillApprovalService
    : IFiscalExceptionSemanticHashControlledBackfillApprovalService
{
    private readonly FiscalExceptionSemanticHashControlledBackfillApprovalOptions _options;

    public FiscalExceptionSemanticHashControlledBackfillApprovalService()
        : this(new FiscalExceptionSemanticHashControlledBackfillApprovalOptions())
    {
    }

    public FiscalExceptionSemanticHashControlledBackfillApprovalService(
        FiscalExceptionSemanticHashControlledBackfillApprovalOptions options)
    {
        _options = options;
    }

    public FiscalExceptionSemanticHashControlledBackfillApprovalResult Evaluate(
        FiscalExceptionSemanticHashControlledBackfillApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);

        var summary = request.Detail.Summary;
        var readiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(summary);
        var mutationStatus = request.LatestRecalculationPreviewAuditSummary?.MutationStatus
            ?? summary.SemanticHashRecalculationMutationStatus;

        if (readiness.Status == FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.NotRequiredCurrent,
                "semantic_hash_already_current_sha256_v1",
                "semantic_hash_controlled_backfill_not_required_current_hash",
                readiness,
                request.LatestRecalculationPreviewAuditSummary,
                mutationStatus);
        }

        if (readiness.Status != FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                readiness.BlockReasonCode ?? "semantic_hash_source_version_incompatible_for_backfill",
                "semantic_hash_controlled_backfill_blocked_non_legacy_hash_state",
                readiness,
                request.LatestRecalculationPreviewAuditSummary,
                mutationStatus);
        }

        if (HasUnsafeFiscalExceptionPosture(summary))
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "fiscal_exception_state_not_safe_for_semantic_hash_backfill_approval",
                "semantic_hash_controlled_backfill_blocked_fiscal_exception_state_not_safe",
                readiness,
                request.LatestRecalculationPreviewAuditSummary,
                mutationStatus);
        }

        var previewAudit = request.LatestRecalculationPreviewAuditSummary;
        if (previewAudit is null)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "semantic_hash_recalculation_preview_audit_missing",
                "semantic_hash_controlled_backfill_blocked_preview_audit_missing",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (previewAudit.LastPreviewStatus != FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                previewAudit.LastBlockReasonCode ?? "semantic_hash_recalculation_preview_not_successful",
                "semantic_hash_controlled_backfill_blocked_preview_not_successful",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (!previewAudit.CompleteOriginalRequestFactsAvailable)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "original_fiscal_request_facts_unavailable",
                "semantic_hash_controlled_backfill_blocked_original_facts_incomplete",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (string.IsNullOrWhiteSpace(previewAudit.RecalculatedHashValue))
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "recalculated_semantic_hash_missing",
                "semantic_hash_controlled_backfill_blocked_recalculated_hash_missing",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (!IsCurrentSha256V1(previewAudit))
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "recalculated_semantic_hash_not_sha256_v1",
                "semantic_hash_controlled_backfill_blocked_recalculated_hash_not_sha256_v1",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (previewAudit.RecalculatedSourceFactCount is null or < 1 ||
            string.IsNullOrWhiteSpace(previewAudit.RecalculatedSafeSourceSummary))
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "recalculated_semantic_hash_metadata_incomplete",
                "semantic_hash_controlled_backfill_blocked_recalculated_hash_metadata_incomplete",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (previewAudit.MutationStatus != FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "semantic_hash_recalculation_preview_mutation_not_proven_absent",
                "semantic_hash_controlled_backfill_blocked_mutation_not_proven_absent",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (!_options.ApprovalPolicyConfigured)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "semantic_hash_backfill_approval_policy_missing",
                "semantic_hash_controlled_backfill_blocked_approval_policy_missing",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (!_options.ActorOrServiceAuthorized)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "semantic_hash_backfill_actor_authorization_missing",
                "semantic_hash_controlled_backfill_blocked_actor_authorization_missing",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (!_options.ExplicitApprovalPresent)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked,
                "semantic_hash_backfill_explicit_approval_missing",
                "semantic_hash_controlled_backfill_blocked_explicit_approval_missing",
                readiness,
                previewAudit,
                mutationStatus);
        }

        if (_options.DualControlRequired && !_options.DualControlSatisfied)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl,
                "semantic_hash_backfill_dual_control_required",
                "semantic_hash_controlled_backfill_pending_dual_control_not_mutated",
                readiness,
                previewAudit,
                mutationStatus);
        }

        return Result(
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            blockReasonCode: null,
            "semantic_hash_controlled_backfill_preconditions_ready_not_mutated",
            readiness,
            previewAudit,
            mutationStatus);
    }

    private static bool HasUnsafeFiscalExceptionPosture(FiscalExceptionQueueCaseSummary summary) =>
        summary.QueueState is FiscalExceptionQueueState.ManualReviewRequired
            or FiscalExceptionQueueState.MismatchReview
            or FiscalExceptionQueueState.Reconciled
            or FiscalExceptionQueueState.Closed ||
        summary.Category is FiscalExceptionQueueCategory.IdempotencyConflict
            or FiscalExceptionQueueCategory.SemanticRequestHashMismatch
            or FiscalExceptionQueueCategory.FiscalMismatch
            or FiscalExceptionQueueCategory.ManualReviewRequired;

    private static bool IsCurrentSha256V1(FiscalExceptionSemanticHashRecalculationPreviewAuditSummary previewAudit) =>
        IsSha256Compatible(previewAudit.RecalculatedHashAlgorithm) &&
        string.Equals(
            previewAudit.RecalculatedHashSourceVersion,
            FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256Compatible(string? hashAlgorithm)
    {
        if (string.IsNullOrWhiteSpace(hashAlgorithm))
        {
            return false;
        }

        var normalized = hashAlgorithm.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Equals(normalized, "SHA256", StringComparison.OrdinalIgnoreCase);
    }

    private FiscalExceptionSemanticHashControlledBackfillApprovalResult Result(
        FiscalExceptionSemanticHashControlledBackfillApprovalStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashReadinessResult readiness,
        FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? previewAudit,
        FiscalExceptionSemanticHashRecalculationMutationStatus mutationStatus)
    {
        var dualControlPosture = !_options.DualControlRequired
            ? FiscalExceptionSemanticHashControlledBackfillDualControlPosture.NotRequired
            : _options.DualControlSatisfied
                ? FiscalExceptionSemanticHashControlledBackfillDualControlPosture.Satisfied
                : FiscalExceptionSemanticHashControlledBackfillDualControlPosture.RequiredPending;

        var approvalPosture = !_options.ApprovalPolicyConfigured
            ? FiscalExceptionSemanticHashControlledBackfillApprovalPosture.PolicyMissing
            : _options.ExplicitApprovalPresent
                ? FiscalExceptionSemanticHashControlledBackfillApprovalPosture.ApprovalPresent
                : FiscalExceptionSemanticHashControlledBackfillApprovalPosture.ApprovalMissing;

        var actorAuthorizationPosture = _options.ActorOrServiceAuthorized
            ? FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture.Present
            : FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture.Missing;

        return new FiscalExceptionSemanticHashControlledBackfillApprovalResult(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            LegacySourceVersion: readiness.StoredSourceVersion,
            RequiredSourceVersion: readiness.RequiredSourceVersion,
            LatestRecalculationPreviewAuditId: previewAudit?.LastRecalculationPreviewAuditId,
            LatestRecalculationPreviewAttemptedAt: previewAudit?.LastAttemptedAt,
            LatestRecalculationPreviewAuditExists: previewAudit is not null,
            PreviewSuccessful: previewAudit?.LastPreviewStatus ==
                FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated,
            CompleteOriginalRequestFactsAvailable: previewAudit?.CompleteOriginalRequestFactsAvailable ?? false,
            RecalculatedHashIsSha256V1: previewAudit is not null && IsCurrentSha256V1(previewAudit),
            RecalculatedHashMetadataComplete: previewAudit is not null &&
                !string.IsNullOrWhiteSpace(previewAudit.RecalculatedHashValue) &&
                previewAudit.RecalculatedSourceFactCount is > 0 &&
                !string.IsNullOrWhiteSpace(previewAudit.RecalculatedSafeSourceSummary),
            DualControlRequired: _options.DualControlRequired,
            DualControlSatisfied: _options.DualControlSatisfied,
            ExplicitApprovalPresent: _options.ExplicitApprovalPresent,
            ActorOrServiceAuthorizationPresent: _options.ActorOrServiceAuthorized,
            DualControlPosture: dualControlPosture,
            ApprovalPosture: approvalPosture,
            ActorAuthorizationPosture: actorAuthorizationPosture,
            MutationStatus: mutationStatus,
            FiscalIssuanceReferenceMutated: false,
            RetryExecutionAvailable: false,
            PosServerPostCalled: false,
            RetryExecuted: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);
    }
}
