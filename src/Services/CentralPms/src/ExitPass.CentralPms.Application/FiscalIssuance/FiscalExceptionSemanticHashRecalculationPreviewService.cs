namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashRecalculationPreviewService
    : IFiscalExceptionSemanticHashRecalculationPreviewService
{
    public const string OriginalFiscalRequestFactsUnavailableReason =
        "original_fiscal_request_facts_unavailable";

    public const string OriginalFiscalRequestFactsIncompleteReason =
        "original_fiscal_request_facts_incomplete";

    private readonly FiscalSemanticRequestHashCalculator _semanticRequestHashCalculator;

    public FiscalExceptionSemanticHashRecalculationPreviewService()
        : this(new FiscalSemanticRequestHashCalculator())
    {
    }

    internal FiscalExceptionSemanticHashRecalculationPreviewService(
        FiscalSemanticRequestHashCalculator semanticRequestHashCalculator)
    {
        _semanticRequestHashCalculator = semanticRequestHashCalculator;
    }

    public FiscalExceptionSemanticHashRecalculationPreviewResult Preview(
        FiscalExceptionSemanticHashRecalculationPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FiscalIssuanceReference);

        var readiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(request.FiscalIssuanceReference);
        if (readiness.Status != FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired)
        {
            return readiness.Status == FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent
                ? Result(
                    FiscalExceptionSemanticHashRecalculationPreviewStatus.NotRequired,
                    blockReasonCode: null,
                    safeSummary: "semantic_hash_recalculation_preview_not_required_current_hash",
                    readiness: readiness,
                    previewAttemptedAt: request.RequestedAt)
                : Result(
                    FiscalExceptionSemanticHashRecalculationPreviewStatus.Unavailable,
                    blockReasonCode: readiness.BlockReasonCode ?? "semantic_hash_recalculation_preview_unavailable",
                    safeSummary: "semantic_hash_recalculation_preview_unavailable_non_legacy_hash_state",
                    readiness: readiness,
                    previewAttemptedAt: request.RequestedAt);
        }

        if (request.OriginalFiscalRequestFacts is null)
        {
            return Result(
                FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked,
                OriginalFiscalRequestFactsUnavailableReason,
                "semantic_hash_recalculation_preview_required_original_facts_unavailable",
                readiness,
                previewAttemptedAt: request.RequestedAt);
        }

        var inspection = _semanticRequestHashCalculator.InspectCanonicalSource(request.OriginalFiscalRequestFacts);
        if (inspection.Status != FiscalSemanticRequestHashSourceStatus.Available ||
            string.IsNullOrWhiteSpace(inspection.HashValue))
        {
            return Result(
                FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked,
                inspection.BlockReasonCode ?? OriginalFiscalRequestFactsIncompleteReason,
                "semantic_hash_recalculation_preview_required_original_facts_incomplete",
                readiness,
                completeOriginalFiscalRequestFactsAvailable: false,
                previewAttemptedAt: request.RequestedAt);
        }

        return Result(
            FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated,
            blockReasonCode: null,
            safeSummary: "semantic_hash_recalculation_preview_calculated_not_mutated",
            readiness: readiness,
            completeOriginalFiscalRequestFactsAvailable: true,
            recalculatedHashValue: inspection.HashValue,
            recalculatedHashAlgorithm: inspection.HashAlgorithm,
            recalculatedHashSourceVersion: inspection.HashSourceVersion,
            recalculatedSourceFactCount: inspection.SourceFactCount,
            recalculatedSafeSourceSummary: inspection.SafeSourceSummary,
            recalculatedHashMatchesStoredHash: string.Equals(
                request.FiscalIssuanceReference.SemanticRequestHashValue,
                inspection.HashValue,
                StringComparison.OrdinalIgnoreCase),
            previewAttemptedAt: request.RequestedAt);
    }

    public static FiscalExceptionSemanticHashRecalculationPreviewResult PreviewWithoutOriginalFacts(
        FiscalIssuanceReferenceRecord fiscalIssuanceReference)
    {
        ArgumentNullException.ThrowIfNull(fiscalIssuanceReference);

        return new FiscalExceptionSemanticHashRecalculationPreviewService().Preview(
            new FiscalExceptionSemanticHashRecalculationPreviewRequest(fiscalIssuanceReference));
    }

    private static FiscalExceptionSemanticHashRecalculationPreviewResult Result(
        FiscalExceptionSemanticHashRecalculationPreviewStatus status,
        string? blockReasonCode,
        string safeSummary,
        FiscalExceptionSemanticHashReadinessResult readiness,
        bool completeOriginalFiscalRequestFactsAvailable = false,
        string? recalculatedHashValue = null,
        string? recalculatedHashAlgorithm = null,
        string? recalculatedHashSourceVersion = null,
        int? recalculatedSourceFactCount = null,
        string? recalculatedSafeSourceSummary = null,
        bool? recalculatedHashMatchesStoredHash = null,
        DateTimeOffset? previewAttemptedAt = null) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            StoredSourceVersion: readiness.StoredSourceVersion,
            RequiredSourceVersion: readiness.RequiredSourceVersion,
            CompleteOriginalFiscalRequestFactsAvailable: completeOriginalFiscalRequestFactsAvailable,
            RecalculatedHashValue: recalculatedHashValue,
            RecalculatedHashAlgorithm: recalculatedHashAlgorithm,
            RecalculatedHashSourceVersion: recalculatedHashSourceVersion,
            RecalculatedSourceFactCount: recalculatedSourceFactCount,
            RecalculatedSafeSourceSummary: recalculatedSafeSourceSummary,
            RecalculatedHashMatchesStoredHash: recalculatedHashMatchesStoredHash,
            PreviewAttemptedAt: previewAttemptedAt,
            MutationStatus: FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated,
            FiscalIssuanceReferenceMutated: false,
            PosServerPostCalled: false,
            RetryExecuted: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false);
}
