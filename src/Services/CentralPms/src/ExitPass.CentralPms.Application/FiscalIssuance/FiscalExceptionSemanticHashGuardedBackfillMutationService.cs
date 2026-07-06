namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashGuardedBackfillMutationService
    : IFiscalExceptionSemanticHashGuardedBackfillMutationService
{
    private readonly FiscalExceptionSemanticHashControlledBackfillMutationOptions _options;
    private readonly IFiscalExceptionSemanticHashGuardedBackfillMutationRepository _repository;

    public FiscalExceptionSemanticHashGuardedBackfillMutationService(
        FiscalExceptionSemanticHashControlledBackfillMutationOptions options,
        IFiscalExceptionSemanticHashGuardedBackfillMutationRepository repository)
    {
        _options = options;
        _repository = repository;
    }

    public Task<FiscalExceptionSemanticHashGuardedBackfillMutationResult> MutateAsync(
        FiscalExceptionSemanticHashGuardedBackfillMutationRequest request,
        CancellationToken cancellationToken)
    {
        var blocked = Validate(request);
        if (blocked is not null)
        {
            return Task.FromResult(blocked);
        }

        if (!_options.EnableControlledMutation)
        {
            return Task.FromResult(Result(
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled,
                "semantic_hash_controlled_backfill_mutation_disabled",
                "semantic_hash_guarded_backfill_mutation_disabled_not_mutated",
                mutationAuditId: request.MutationPreparationBasis.MutationAuditId,
                oldSourceVersion: request.Detail.Summary.SemanticRequestHashSourceVersion,
                newSourceVersion: null,
                oldHashValue: request.Detail.Summary.SemanticRequestHashValue,
                newHashValue: null,
                mutationTimestamp: null,
                mutated: false));
        }

        var command = request.MutationPreparationBasis.Command!;
        if (command.DryRunOnly)
        {
            return Task.FromResult(Result(
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Disabled,
                "semantic_hash_controlled_backfill_mutation_dry_run_only",
                "semantic_hash_guarded_backfill_mutation_dry_run_not_mutated",
                mutationAuditId: request.MutationPreparationBasis.MutationAuditId,
                oldSourceVersion: command.StoredSourceVersion,
                newSourceVersion: command.RecalculatedHashSourceVersion,
                oldHashValue: request.Detail.Summary.SemanticRequestHashValue,
                newHashValue: command.RecalculatedHashValue,
                mutationTimestamp: null,
                mutated: false));
        }

        return _repository.MutateAsync(
            new FiscalExceptionSemanticHashGuardedBackfillMutationCommand(
                FiscalIssuanceReferenceId: command.FiscalIssuanceReferenceId,
                RecalculationPreviewAuditId: command.LatestRecalculationPreviewAuditId,
                MutationPreparationAuditId: request.MutationPreparationBasis.MutationAuditId!.Value,
                ApprovalBasisStatus: request.ApprovalBasis.Status,
                ExpectedOldSourceVersion: command.StoredSourceVersion
                    ?? FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion,
                RequiredSourceVersion: command.RequiredSourceVersion,
                OldHashValue: request.Detail.Summary.SemanticRequestHashValue,
                NewHashValue: command.RecalculatedHashValue,
                NewHashAlgorithm: command.RecalculatedHashAlgorithm,
                NewHashSourceVersion: command.RecalculatedHashSourceVersion,
                NewHashSourceFactCount: command.RecalculatedSourceFactCount,
                SafeSourceSummary: command.RecalculatedSafeSourceSummary,
                ActorServiceIdentityId: request.ActorServiceIdentityId,
                ApprovalReference: request.ApprovalReference,
                DualControlReference: request.DualControlReference,
                CorrelationId: command.CorrelationId,
                AttemptedAt: request.RequestedAt ?? DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static FiscalExceptionSemanticHashGuardedBackfillMutationResult? Validate(
        FiscalExceptionSemanticHashGuardedBackfillMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);
        ArgumentNullException.ThrowIfNull(request.ApprovalBasis);
        ArgumentNullException.ThrowIfNull(request.LatestRecalculationPreviewAuditSummary);
        ArgumentNullException.ThrowIfNull(request.MutationPreparationBasis);

        var summary = request.Detail.Summary;
        var approval = request.ApprovalBasis;
        var preview = request.LatestRecalculationPreviewAuditSummary;
        var preparation = request.MutationPreparationBasis;

        if (summary.FiscalIssuanceReferenceId == Guid.Empty)
        {
            return Blocked("fiscal_issuance_reference_id_required", "semantic_hash_guarded_backfill_blocked_reference_missing", summary);
        }

        if (approval.Status != FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill)
        {
            return Blocked(
                approval.BlockReasonCode ?? "semantic_hash_controlled_backfill_approval_not_ready",
                "semantic_hash_guarded_backfill_blocked_approval_not_ready",
                summary);
        }

        if (!approval.ActorOrServiceAuthorizationPresent || request.ActorServiceIdentityId == Guid.Empty)
        {
            return Blocked(
                "semantic_hash_backfill_actor_authorization_missing",
                "semantic_hash_guarded_backfill_blocked_actor_authorization_missing",
                summary);
        }

        if (!approval.ExplicitApprovalPresent || string.IsNullOrWhiteSpace(request.ApprovalReference))
        {
            return Blocked(
                "semantic_hash_backfill_explicit_approval_missing",
                "semantic_hash_guarded_backfill_blocked_explicit_approval_missing",
                summary);
        }

        if (approval.DualControlRequired &&
            (!approval.DualControlSatisfied || string.IsNullOrWhiteSpace(request.DualControlReference)))
        {
            return Blocked(
                "semantic_hash_backfill_dual_control_required",
                "semantic_hash_guarded_backfill_blocked_dual_control_required",
                summary);
        }

        if (preview.LastPreviewStatus != FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated)
        {
            return Blocked(
                preview.LastBlockReasonCode ?? "semantic_hash_recalculation_preview_not_successful",
                "semantic_hash_guarded_backfill_blocked_preview_not_successful",
                summary);
        }

        if (preview.LastRecalculationPreviewAuditId == Guid.Empty ||
            approval.LatestRecalculationPreviewAuditId != preview.LastRecalculationPreviewAuditId)
        {
            return Stale(
                "semantic_hash_recalculation_preview_audit_basis_mismatch",
                "semantic_hash_guarded_backfill_stale_preview_audit_basis_mismatch",
                summary);
        }

        if (!preview.CompleteOriginalRequestFactsAvailable || !approval.CompleteOriginalRequestFactsAvailable)
        {
            return Blocked(
                "original_fiscal_request_facts_unavailable",
                "semantic_hash_guarded_backfill_blocked_original_facts_incomplete",
                summary);
        }

        if (!IsCurrentSha256V1(preview) ||
            string.IsNullOrWhiteSpace(preview.RecalculatedHashValue) ||
            preview.RecalculatedSourceFactCount is null or < 1 ||
            string.IsNullOrWhiteSpace(preview.RecalculatedSafeSourceSummary))
        {
            return Blocked(
                "recalculated_semantic_hash_metadata_incomplete",
                "semantic_hash_guarded_backfill_blocked_recalculated_hash_metadata_incomplete",
                summary);
        }

        if (preparation.MutationAuditId is null || preparation.MutationAuditId == Guid.Empty)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_preparation_audit_missing",
                "semantic_hash_guarded_backfill_blocked_mutation_preparation_audit_missing",
                summary);
        }

        if (!preparation.AuditPersisted ||
            preparation.Command is null ||
            preparation.Status !=
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_preparation_not_ready",
                "semantic_hash_guarded_backfill_blocked_mutation_preparation_not_ready",
                summary);
        }

        if (preparation.Command.MutationMode != FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly ||
            preparation.MutationMode != FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly)
        {
            return Blocked(
                "semantic_hash_backfill_mutation_mode_not_single_record",
                "semantic_hash_guarded_backfill_blocked_non_single_record_mode",
                summary);
        }

        if (preparation.Command.LatestRecalculationPreviewAuditId != preview.LastRecalculationPreviewAuditId ||
            preparation.Command.RecalculatedHashValue != preview.RecalculatedHashValue ||
            !string.Equals(
                preparation.Command.RecalculatedHashAlgorithm,
                preview.RecalculatedHashAlgorithm,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                preparation.Command.RecalculatedHashSourceVersion,
                preview.RecalculatedHashSourceVersion,
                StringComparison.OrdinalIgnoreCase) ||
            preparation.Command.RecalculatedSourceFactCount != preview.RecalculatedSourceFactCount ||
            preparation.Command.RecalculatedSafeSourceSummary != preview.RecalculatedSafeSourceSummary)
        {
            return Stale(
                "semantic_hash_backfill_mutation_preparation_basis_mismatch",
                "semantic_hash_guarded_backfill_stale_mutation_preparation_basis_mismatch",
                summary);
        }

        var readiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(summary);
        if (readiness.Status == FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent)
        {
            return Blocked(
                "semantic_hash_already_current_sha256_v1",
                "semantic_hash_guarded_backfill_blocked_already_current",
                summary);
        }

        if (readiness.Status != FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired)
        {
            return Blocked(
                readiness.BlockReasonCode ?? "semantic_hash_source_version_incompatible_for_backfill",
                "semantic_hash_guarded_backfill_blocked_non_legacy_hash_state",
                summary);
        }

        if (HasUnsafeFiscalExceptionPosture(summary))
        {
            return Blocked(
                "fiscal_exception_state_not_safe_for_semantic_hash_backfill_mutation",
                "semantic_hash_guarded_backfill_blocked_fiscal_exception_state_not_safe",
                summary);
        }

        return null;
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

    private static bool IsCurrentSha256V1(FiscalExceptionSemanticHashRecalculationPreviewAuditSummary previewAudit)
    {
        if (string.IsNullOrWhiteSpace(previewAudit.RecalculatedHashAlgorithm) ||
            string.IsNullOrWhiteSpace(previewAudit.RecalculatedHashSourceVersion))
        {
            return false;
        }

        var normalized = previewAudit.RecalculatedHashAlgorithm
            .Trim()
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Equals(normalized, "SHA256", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                previewAudit.RecalculatedHashSourceVersion,
                FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                StringComparison.OrdinalIgnoreCase);
    }

    private static FiscalExceptionSemanticHashGuardedBackfillMutationResult Blocked(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionQueueCaseSummary summary) =>
        Result(
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked,
            blockReasonCode,
            safeSummary,
            mutationAuditId: null,
            oldSourceVersion: summary.SemanticRequestHashSourceVersion,
            newSourceVersion: null,
            oldHashValue: summary.SemanticRequestHashValue,
            newHashValue: null,
            mutationTimestamp: null,
            mutated: false);

    private static FiscalExceptionSemanticHashGuardedBackfillMutationResult Stale(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionQueueCaseSummary summary) =>
        Result(
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Stale,
            blockReasonCode,
            safeSummary,
            mutationAuditId: null,
            oldSourceVersion: summary.SemanticRequestHashSourceVersion,
            newSourceVersion: null,
            oldHashValue: summary.SemanticRequestHashValue,
            newHashValue: null,
            mutationTimestamp: null,
            mutated: false);

    public static FiscalExceptionSemanticHashGuardedBackfillMutationResult Result(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status,
        string? blockReasonCode,
        string safeSummary,
        Guid? mutationAuditId,
        string? oldSourceVersion,
        string? newSourceVersion,
        string? oldHashValue,
        string? newHashValue,
        DateTimeOffset? mutationTimestamp,
        bool mutated) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            MutationAuditId: mutationAuditId,
            OldSourceVersion: oldSourceVersion,
            NewSourceVersion: newSourceVersion,
            OldHashValue: oldHashValue,
            NewHashValue: newHashValue,
            MutationTimestamp: mutationTimestamp,
            FiscalIssuanceReferenceMutated: mutated,
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
