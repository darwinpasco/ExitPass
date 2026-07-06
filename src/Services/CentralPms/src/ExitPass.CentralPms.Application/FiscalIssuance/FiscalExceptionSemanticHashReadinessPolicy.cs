namespace ExitPass.CentralPms.Application.FiscalIssuance;

public static class FiscalExceptionSemanticHashReadinessPolicy
{
    public const string LegacyCentralPmsHashSourceVersion = "central-pms-pos-server-fiscal-request-v1";

    public static FiscalExceptionSemanticHashReadinessResult Evaluate(
        FiscalIssuanceReferenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Evaluate(
            record.SemanticRequestHashStatus,
            ToAvailabilityStatus(record),
            record.SemanticRequestHashValue,
            record.SemanticRequestHashAlgorithm,
            record.SemanticRequestHashSourceVersion,
            record.SemanticRequestHashSourceFactCount,
            record.SemanticRequestHashSafeSummary);
    }

    public static FiscalExceptionSemanticHashReadinessResult Evaluate(
        FiscalExceptionQueueCaseSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return Evaluate(
            sourceStatus: null,
            summary.SemanticRequestHashAvailabilityStatus,
            summary.SemanticRequestHashValue,
            summary.SemanticRequestHashAlgorithm,
            summary.SemanticRequestHashSourceVersion,
            summary.SemanticRequestHashSourceFactCount,
            summary.SafeSemanticRequestHashSourceSummary);
    }

    public static FiscalExceptionSemanticHashReadinessResult Evaluate(
        FiscalSemanticRequestHashSourceStatus? sourceStatus,
        FiscalExceptionSemanticRequestHashAvailabilityStatus availabilityStatus,
        string? hashValue,
        string? hashAlgorithm,
        string? sourceVersion,
        int? sourceFactCount,
        string? safeSourceSummary)
    {
        var storedSourceVersion = string.IsNullOrWhiteSpace(sourceVersion)
            ? null
            : sourceVersion.Trim();

        if (IsLegacyVersion(storedSourceVersion))
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired,
                "semantic_hash_legacy_version_requires_recalculation",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_legacy_version_requires_recalculation_no_retry");
        }

        if (sourceStatus == FiscalSemanticRequestHashSourceStatus.Unavailable)
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Unavailable,
                "semantic_hash_unavailable",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_unavailable_no_retry");
        }

        if (sourceStatus == FiscalSemanticRequestHashSourceStatus.Incomplete ||
            availabilityStatus == FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButUnconfirmed)
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Incomplete,
                "semantic_hash_incomplete",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_incomplete_no_retry");
        }

        if (string.IsNullOrWhiteSpace(hashValue))
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Missing,
                "semantic_hash_value_missing",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_value_missing_no_retry");
        }

        if (string.IsNullOrWhiteSpace(hashAlgorithm))
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Missing,
                "semantic_hash_algorithm_missing",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_algorithm_missing_no_retry");
        }

        if (storedSourceVersion is null)
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Missing,
                "semantic_hash_source_version_missing",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_source_version_missing_no_retry");
        }

        if (!IsSha256Compatible(hashAlgorithm))
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Incompatible,
                "semantic_hash_algorithm_incompatible",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_algorithm_incompatible_no_retry");
        }

        if (!string.Equals(
                storedSourceVersion,
                FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Incompatible,
                "semantic_hash_source_version_incompatible",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_source_version_incompatible_no_retry");
        }

        if (sourceFactCount is null or < 1 || string.IsNullOrWhiteSpace(safeSourceSummary))
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Incomplete,
                "semantic_hash_source_summary_or_fact_count_missing",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_source_summary_or_fact_count_missing_no_retry");
        }

        if (availabilityStatus == FiscalExceptionSemanticRequestHashAvailabilityStatus.NotAvailableInCurrentModel)
        {
            return Result(
                FiscalExceptionSemanticHashReadinessStatus.Unavailable,
                "semantic_hash_unavailable",
                storedSourceVersion,
                FiscalExceptionSemanticHashRecalculationPosture.Unknown,
                "semantic_hash_unavailable_no_retry");
        }

        return Result(
            FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent,
            blockReasonCode: null,
            storedSourceVersion,
            FiscalExceptionSemanticHashRecalculationPosture.Unknown,
            "semantic_hash_ready_current_sha256_v1_no_retry_execution");
    }

    public static bool IsReady(FiscalExceptionSemanticHashReadinessStatus status) =>
        status == FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent;

    public static FiscalExceptionSemanticRequestHashAvailabilityStatus ToAvailabilityStatus(
        FiscalIssuanceReferenceRecord record) =>
        record.SemanticRequestHashStatus switch
        {
            FiscalSemanticRequestHashSourceStatus.Available
                when !string.IsNullOrWhiteSpace(record.SemanticRequestHashValue) &&
                    !string.IsNullOrWhiteSpace(record.SemanticRequestHashAlgorithm) &&
                    !string.IsNullOrWhiteSpace(record.SemanticRequestHashSourceVersion) =>
                FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed,
            FiscalSemanticRequestHashSourceStatus.Incomplete =>
                FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButUnconfirmed,
            FiscalSemanticRequestHashSourceStatus.Unavailable =>
                FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing,
            _ => FiscalExceptionSemanticRequestHashAvailabilityStatus.NotAvailableInCurrentModel
        };

    public static FiscalExceptionSemanticRequestHashAvailabilityStatus ToAvailabilityStatus(
        FiscalExceptionSemanticHashReadinessStatus readinessStatus) =>
        readinessStatus switch
        {
            FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent =>
                FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed,
            FiscalExceptionSemanticHashReadinessStatus.Missing or
                FiscalExceptionSemanticHashReadinessStatus.Unavailable =>
                FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButMissing,
            _ => FiscalExceptionSemanticRequestHashAvailabilityStatus.RequiredButUnconfirmed
        };

    private static bool IsLegacyVersion(string? sourceVersion) =>
        string.Equals(
            sourceVersion,
            LegacyCentralPmsHashSourceVersion,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256Compatible(string hashAlgorithm)
    {
        var normalized = hashAlgorithm.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Equals(normalized, "SHA256", StringComparison.OrdinalIgnoreCase);
    }

    private static FiscalExceptionSemanticHashReadinessResult Result(
        FiscalExceptionSemanticHashReadinessStatus status,
        string? blockReasonCode,
        string? storedSourceVersion,
        FiscalExceptionSemanticHashRecalculationPosture recalculationPosture,
        string safeSummary) =>
        new(
            Status: status,
            BlockReasonCode: blockReasonCode,
            StoredSourceVersion: storedSourceVersion,
            RequiredSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            RecalculationPosture: recalculationPosture,
            SafeSummary: safeSummary);
}
