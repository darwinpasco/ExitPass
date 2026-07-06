namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashControlledBackfillMutationPreparationService
    : IFiscalExceptionSemanticHashControlledBackfillMutationPreparationService
{
    private readonly FiscalExceptionSemanticHashControlledBackfillMutationOptions _options;
    private readonly IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository? _auditRepository;

    public FiscalExceptionSemanticHashControlledBackfillMutationPreparationService()
        : this(new FiscalExceptionSemanticHashControlledBackfillMutationOptions(), null)
    {
    }

    public FiscalExceptionSemanticHashControlledBackfillMutationPreparationService(
        FiscalExceptionSemanticHashControlledBackfillMutationOptions options,
        IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository? auditRepository = null)
    {
        _options = options;
        _auditRepository = auditRepository;
    }

    public async Task<FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult> PrepareAsync(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest request,
        CancellationToken cancellationToken)
    {
        var evaluated = Evaluate(request);
        if (_auditRepository is null)
        {
            return evaluated.Status == FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus
                    .PreparedButMutationDisabled
                ? Result(
                    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable,
                    "semantic_hash_backfill_mutation_audit_persistence_unavailable",
                    "semantic_hash_backfill_mutation_unavailable_audit_persistence_unavailable",
                    command: null,
                    mutationEnabled: _options.EnableControlledMutation,
                    dryRunOnly: request.DryRunOnly)
                : evaluated;
        }

        try
        {
            var record = await _auditRepository.RecordAsync(
                ToAuditWrite(request, evaluated),
                cancellationToken);

            return evaluated with
            {
                AuditPersisted = true,
                MutationAuditId = record.MutationAuditId,
                MutationAttemptedAt = record.AttemptedAt
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "semantic_hash_controlled_backfill_mutation_audit_persistence_failed",
                ex);
        }
    }

    private FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult Evaluate(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);
        ArgumentNullException.ThrowIfNull(request.ApprovalBasis);

        var detail = request.Detail;
        var summary = detail.Summary;
        var approval = request.ApprovalBasis;
        var previewAudit = request.LatestRecalculationPreviewAuditSummary;

        if (approval.Status != FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill)
        {
            return Blocked(
                approval.BlockReasonCode ?? "semantic_hash_controlled_backfill_approval_not_ready",
                "semantic_hash_backfill_mutation_blocked_approval_not_ready",
                request);
        }

        if (previewAudit is null)
        {
            return Blocked(
                "semantic_hash_recalculation_preview_audit_missing",
                "semantic_hash_backfill_mutation_blocked_preview_audit_missing",
                request);
        }

        if (previewAudit.LastPreviewStatus != FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated ||
            !approval.PreviewSuccessful)
        {
            return Blocked(
                previewAudit.LastBlockReasonCode ?? "semantic_hash_recalculation_preview_not_successful",
                "semantic_hash_backfill_mutation_blocked_preview_not_successful",
                request);
        }

        if (!previewAudit.CompleteOriginalRequestFactsAvailable ||
            !approval.CompleteOriginalRequestFactsAvailable)
        {
            return Blocked(
                "original_fiscal_request_facts_unavailable",
                "semantic_hash_backfill_mutation_blocked_original_facts_incomplete",
                request);
        }

        if (string.IsNullOrWhiteSpace(previewAudit.RecalculatedHashValue))
        {
            return Blocked(
                "recalculated_semantic_hash_missing",
                "semantic_hash_backfill_mutation_blocked_recalculated_hash_missing",
                request);
        }

        if (!IsCurrentSha256V1(previewAudit) || !approval.RecalculatedHashIsSha256V1)
        {
            return Blocked(
                "recalculated_semantic_hash_not_sha256_v1",
                "semantic_hash_backfill_mutation_blocked_recalculated_hash_not_sha256_v1",
                request);
        }

        if (previewAudit.RecalculatedSourceFactCount is null or < 1 ||
            string.IsNullOrWhiteSpace(previewAudit.RecalculatedSafeSourceSummary) ||
            !approval.RecalculatedHashMetadataComplete)
        {
            return Blocked(
                "recalculated_semantic_hash_metadata_incomplete",
                "semantic_hash_backfill_mutation_blocked_recalculated_hash_metadata_incomplete",
                request);
        }

        if (previewAudit.MutationStatus != FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated ||
            approval.MutationStatus != FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated)
        {
            return Blocked(
                "semantic_hash_recalculation_preview_mutation_not_proven_absent",
                "semantic_hash_backfill_mutation_blocked_mutation_not_proven_absent",
                request);
        }

        if (!approval.ActorOrServiceAuthorizationPresent)
        {
            return Blocked(
                "semantic_hash_backfill_actor_authorization_missing",
                "semantic_hash_backfill_mutation_blocked_actor_authorization_missing",
                request);
        }

        if (!approval.ExplicitApprovalPresent)
        {
            return Blocked(
                "semantic_hash_backfill_explicit_approval_missing",
                "semantic_hash_backfill_mutation_blocked_explicit_approval_missing",
                request);
        }

        if (approval.DualControlRequired && !approval.DualControlSatisfied)
        {
            return Blocked(
                "semantic_hash_backfill_dual_control_required",
                "semantic_hash_backfill_mutation_blocked_dual_control_required",
                request);
        }

        var readiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(summary);
        if (readiness.Status == FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent)
        {
            return Blocked(
                "semantic_hash_already_current_sha256_v1",
                "semantic_hash_backfill_mutation_blocked_already_current",
                request);
        }

        if (readiness.Status != FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired)
        {
            return Blocked(
                readiness.BlockReasonCode ?? "semantic_hash_source_version_incompatible_for_backfill",
                "semantic_hash_backfill_mutation_blocked_non_legacy_hash_state",
                request);
        }

        if (HasUnsafeFiscalExceptionPosture(summary))
        {
            return Blocked(
                "fiscal_exception_state_not_safe_for_semantic_hash_backfill_mutation",
                "semantic_hash_backfill_mutation_blocked_fiscal_exception_state_not_safe",
                request);
        }

        var command = new FiscalExceptionSemanticHashControlledBackfillMutationCommand(
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            LatestRecalculationPreviewAuditId: previewAudit.LastRecalculationPreviewAuditId,
            ApprovalBasisStatus: approval.Status,
            StoredSourceVersion: readiness.StoredSourceVersion,
            RequiredSourceVersion: readiness.RequiredSourceVersion,
            RecalculatedHashValue: previewAudit.RecalculatedHashValue!,
            RecalculatedHashAlgorithm: previewAudit.RecalculatedHashAlgorithm!,
            RecalculatedHashSourceVersion: previewAudit.RecalculatedHashSourceVersion!,
            RecalculatedSourceFactCount: previewAudit.RecalculatedSourceFactCount!.Value,
            RecalculatedSafeSourceSummary: previewAudit.RecalculatedSafeSourceSummary!,
            ActorServiceIdentityId: request.ActorServiceIdentityId,
            ApprovalReference: request.ApprovalReference,
            DualControlReference: request.DualControlReference,
            CorrelationId: detail.CorrelationId,
            MutationMode: FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            DryRunOnly: request.DryRunOnly,
            MutationStatus: _options.EnableControlledMutation
                ? FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation
                : FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled);

        if (_options.EnableControlledMutation)
        {
            return Result(
                FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedForControlledMutation,
                blockReasonCode: null,
                "semantic_hash_backfill_mutation_prepared_single_record_guarded_write_enabled",
                command,
                mutationEnabled: true,
                dryRunOnly: request.DryRunOnly);
        }

        return Result(
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled,
            blockReasonCode: "semantic_hash_controlled_backfill_mutation_disabled",
            "semantic_hash_backfill_mutation_prepared_single_record_disabled_not_mutated",
            command,
            mutationEnabled: false,
            dryRunOnly: request.DryRunOnly);
    }

    private static FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite ToAuditWrite(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest request,
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult result)
    {
        var summary = request.Detail.Summary;
        var previewAudit = request.LatestRecalculationPreviewAuditSummary;

        return new FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite(
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            RecalculationPreviewAuditId: previewAudit?.LastRecalculationPreviewAuditId,
            MutationPreparationAuditId: null,
            ApprovalBasisStatus: request.ApprovalBasis.Status,
            OldSourceVersion: summary.SemanticRequestHashSourceVersion,
            RequiredSourceVersion: summary.RequiredSemanticHashSourceVersion,
            OldHashValue: summary.SemanticRequestHashValue,
            NewHashValue: previewAudit?.RecalculatedHashValue,
            NewHashAlgorithm: previewAudit?.RecalculatedHashAlgorithm,
            NewHashSourceVersion: previewAudit?.RecalculatedHashSourceVersion,
            NewHashSourceFactCount: previewAudit?.RecalculatedSourceFactCount,
            SafeSourceSummary: previewAudit?.RecalculatedSafeSourceSummary,
            MutationStatus: result.Status,
            BlockReasonCode: result.BlockReasonCode,
            MutationMode: result.MutationMode,
            MutationEnabled: result.MutationEnabled,
            FiscalIssuanceReferenceMutated: false,
            AttemptedAt: result.MutationAttemptedAt ?? request.RequestedAt ?? DateTimeOffset.UtcNow,
            SafeSummary: result.SafeSummary,
            CorrelationId: request.Detail.CorrelationId,
            ActorServiceIdentityId: request.ActorServiceIdentityId,
            ApprovalReference: request.ApprovalReference,
            DualControlReference: request.DualControlReference);
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

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult Blocked(
        string blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest request) =>
        Result(
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked,
            blockReasonCode,
            safeSummary,
            command: null,
            mutationEnabled: false,
            dryRunOnly: request.DryRunOnly);

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult Result(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashControlledBackfillMutationCommand? command,
        bool mutationEnabled,
        bool dryRunOnly) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            Command: command,
            MutationMode: FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            MutationEnabled: mutationEnabled,
            DryRunOnly: dryRunOnly,
            AuditPersisted: false,
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
