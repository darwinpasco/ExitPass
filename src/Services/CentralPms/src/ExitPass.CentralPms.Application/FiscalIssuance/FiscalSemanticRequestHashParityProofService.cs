namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalSemanticRequestHashParityProofService : IFiscalSemanticRequestHashParityProofService
{
    private readonly FiscalSemanticRequestHashCalculator _calculator;

    public FiscalSemanticRequestHashParityProofService()
        : this(new FiscalSemanticRequestHashCalculator())
    {
    }

    internal FiscalSemanticRequestHashParityProofService(FiscalSemanticRequestHashCalculator calculator)
    {
        _calculator = calculator;
    }

    public FiscalSemanticRequestHashParityProofResult Prove(
        PosServerFiscalDocumentCreateRequest request,
        FiscalSemanticRequestHashParityFixture? posServerExpected)
    {
        ArgumentNullException.ThrowIfNull(request);

        var centralPms = _calculator.InspectCanonicalSource(request);
        if (centralPms.Status != FiscalSemanticRequestHashSourceStatus.Available)
        {
            return Result(
                FiscalSemanticRequestHashParityProofStatus.Unavailable,
                centralPms,
                posServerExpected,
                centralPms.BlockReasonCode ?? "central_pms_semantic_hash_source_unavailable",
                "semantic_hash_parity_unavailable_central_pms_source_incomplete");
        }

        if (posServerExpected is null ||
            string.IsNullOrWhiteSpace(posServerExpected.PosServerCanonicalSourceText) ||
            string.IsNullOrWhiteSpace(posServerExpected.PosServerSemanticRequestHash))
        {
            return Result(
                FiscalSemanticRequestHashParityProofStatus.Unconfirmed,
                centralPms,
                posServerExpected,
                "pos_server_hash_source_code_not_available_for_parity_proof",
                "semantic_hash_parity_unconfirmed_pos_server_source_unavailable");
        }

        if (!string.Equals(
                posServerExpected.PosServerHashSourceVersion.Trim(),
                FiscalExceptionPosServerRetryContractReadinessService.PosServerSemanticHashVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                FiscalSemanticRequestHashParityProofStatus.Unconfirmed,
                centralPms,
                posServerExpected,
                "pos_server_semantic_hash_parity_unproven",
                "semantic_hash_parity_unconfirmed_pos_server_source_version_not_sha256_v1");
        }

        if (!string.Equals(
                centralPms.CanonicalSourceText,
                posServerExpected.PosServerCanonicalSourceText,
                StringComparison.Ordinal) ||
            !string.Equals(
                centralPms.HashValue,
                posServerExpected.PosServerSemanticRequestHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                FiscalSemanticRequestHashParityProofStatus.Mismatch,
                centralPms,
                posServerExpected,
                "pos_server_semantic_hash_mismatch",
                "semantic_hash_parity_mismatch_pos_server_expected_hash_or_source_differs");
        }

        return Result(
            FiscalSemanticRequestHashParityProofStatus.Proven,
            centralPms,
            posServerExpected,
            null,
            "semantic_hash_parity_proven_sha256_v1_no_execution");
    }

    private static FiscalSemanticRequestHashParityProofResult Result(
        FiscalSemanticRequestHashParityProofStatus status,
        FiscalSemanticRequestHashCanonicalInspectionResult centralPms,
        FiscalSemanticRequestHashParityFixture? posServerExpected,
        string? blockReasonCode,
        string safeSummary) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            SafeSummary: safeSummary,
            CentralPmsHashSourceVersion: centralPms.HashSourceVersion,
            CentralPmsCanonicalSourceText: centralPms.CanonicalSourceText,
            CentralPmsNormalizedFacts: centralPms.NormalizedFacts,
            CentralPmsSemanticRequestHash: centralPms.HashValue,
            PosServerExpectedHashSourceVersion: posServerExpected?.PosServerHashSourceVersion,
            PosServerExpectedCanonicalSourceText: posServerExpected?.PosServerCanonicalSourceText,
            PosServerExpectedSemanticRequestHash: posServerExpected?.PosServerSemanticRequestHash);
}
