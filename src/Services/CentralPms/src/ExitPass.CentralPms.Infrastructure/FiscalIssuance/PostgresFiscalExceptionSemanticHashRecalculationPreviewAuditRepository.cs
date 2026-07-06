using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionSemanticHashRecalculationPreviewAuditRepository :
    IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionSemanticHashRecalculationPreviewAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionSemanticHashRecalculationPreviewAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashRecalculationPreviewAuditWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_semantic_hash_recalculation_previews (
                fiscal_issuance_reference_id,
                stored_semantic_hash_source_version,
                required_semantic_hash_source_version,
                stored_semantic_hash_value,
                recalculation_preview_status,
                recalculation_block_reason_code,
                complete_original_request_facts_available,
                recalculated_hash_value,
                recalculated_hash_algorithm,
                recalculated_hash_source_version,
                recalculated_source_fact_count,
                safe_source_summary,
                recalculated_hash_matches_stored,
                mutation_status,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @stored_semantic_hash_source_version,
                @required_semantic_hash_source_version,
                @stored_semantic_hash_value,
                @recalculation_preview_status,
                @recalculation_block_reason_code,
                @complete_original_request_facts_available,
                @recalculated_hash_value,
                @recalculated_hash_algorithm,
                @recalculated_hash_source_version,
                @recalculated_source_fact_count,
                @safe_source_summary,
                @recalculated_hash_matches_stored,
                @mutation_status,
                @attempted_at,
                @safe_summary,
                @correlation_id,
                @actor_service_identity_id
            )
            RETURNING
                semantic_hash_recalculation_preview_audit_id,
                fiscal_issuance_reference_id,
                stored_semantic_hash_source_version,
                required_semantic_hash_source_version,
                stored_semantic_hash_value,
                recalculation_preview_status,
                recalculation_block_reason_code,
                complete_original_request_facts_available,
                recalculated_hash_value,
                recalculated_hash_algorithm,
                recalculated_hash_source_version,
                recalculated_source_fact_count,
                safe_source_summary,
                recalculated_hash_matches_stored,
                mutation_status,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                created_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        AddNullable(command, "stored_semantic_hash_source_version", TruncateOrNull(attempt.StoredSemanticHashSourceVersion, 80));
        command.Parameters.AddWithValue(
            "required_semantic_hash_source_version",
            Truncate(attempt.RequiredSemanticHashSourceVersion, 80));
        AddNullable(command, "stored_semantic_hash_value", TruncateOrNull(attempt.StoredSemanticHashValue, 64));
        command.Parameters.AddWithValue("recalculation_preview_status", ToStorageValue(attempt.PreviewStatus));
        AddNullable(command, "recalculation_block_reason_code", TruncateOrNull(attempt.BlockReasonCode, 160));
        command.Parameters.AddWithValue(
            "complete_original_request_facts_available",
            attempt.CompleteOriginalRequestFactsAvailable);
        AddNullable(command, "recalculated_hash_value", TruncateOrNull(attempt.RecalculatedHashValue, 64));
        AddNullable(command, "recalculated_hash_algorithm", TruncateOrNull(attempt.RecalculatedHashAlgorithm, 32));
        AddNullable(command, "recalculated_hash_source_version", TruncateOrNull(attempt.RecalculatedHashSourceVersion, 80));
        AddNullable(command, "recalculated_source_fact_count", attempt.RecalculatedSourceFactCount);
        AddNullable(command, "safe_source_summary", TruncateOrNull(attempt.RecalculatedSafeSourceSummary, 240));
        AddNullable(command, "recalculated_hash_matches_stored", attempt.RecalculatedHashMatchesStoredHash);
        command.Parameters.AddWithValue("mutation_status", ToStorageValue(attempt.MutationStatus));
        command.Parameters.AddWithValue("attempted_at", attempt.AttemptedAt);
        command.Parameters.AddWithValue("safe_summary", Truncate(attempt.SafeSummary, 240));
        AddNullable(command, "correlation_id", attempt.CorrelationId);
        AddNullable(command, "actor_service_identity_id", attempt.ServiceIdentityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Semantic hash recalculation preview audit insert returned no rows.");
        }

        return MapRecord(reader);
    }

    public async Task<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken)
    {
        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Fiscal issuance reference id is required.",
                nameof(fiscalIssuanceReferenceId));
        }

        const string sql = """
            SELECT
                semantic_hash_recalculation_preview_audit_id,
                recalculation_preview_status,
                recalculation_block_reason_code,
                complete_original_request_facts_available,
                recalculated_hash_value,
                recalculated_hash_algorithm,
                recalculated_hash_source_version,
                recalculated_source_fact_count,
                safe_source_summary,
                recalculated_hash_matches_stored,
                mutation_status,
                attempted_at,
                safe_summary,
                COUNT(*) OVER ()::integer AS attempt_count
            FROM core.fiscal_issuance_semantic_hash_recalculation_previews
            WHERE fiscal_issuance_reference_id = @fiscal_issuance_reference_id
            ORDER BY attempted_at DESC, created_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("fiscal_issuance_reference_id", fiscalIssuanceReferenceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FiscalExceptionSemanticHashRecalculationPreviewAuditSummary(
            LastRecalculationPreviewAuditId: reader.GetGuid(
                reader.GetOrdinal("semantic_hash_recalculation_preview_audit_id")),
            LastPreviewStatus: ParsePreviewStatus(reader.GetString(reader.GetOrdinal("recalculation_preview_status"))),
            LastAttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            AttemptCount: reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastBlockReasonCode: GetNullableString(reader, "recalculation_block_reason_code"),
            CompleteOriginalRequestFactsAvailable: reader.GetBoolean(
                reader.GetOrdinal("complete_original_request_facts_available")),
            RecalculatedHashValue: GetNullableString(reader, "recalculated_hash_value"),
            RecalculatedHashAlgorithm: GetNullableString(reader, "recalculated_hash_algorithm"),
            RecalculatedHashSourceVersion: GetNullableString(reader, "recalculated_hash_source_version"),
            RecalculatedSourceFactCount: GetNullableInt32(reader, "recalculated_source_fact_count"),
            RecalculatedSafeSourceSummary: GetNullableString(reader, "safe_source_summary"),
            RecalculatedHashMatchesStoredHash: GetNullableBoolean(reader, "recalculated_hash_matches_stored"),
            MutationStatus: ParseMutationStatus(reader.GetString(reader.GetOrdinal("mutation_status"))),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")));
    }

    private static void Validate(FiscalExceptionSemanticHashRecalculationPreviewAuditWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.RequiredSemanticHashSourceVersion))
        {
            throw new ArgumentException("Required semantic hash source version is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeSummary))
        {
            throw new ArgumentException("Semantic hash recalculation preview safe summary is required.", nameof(attempt));
        }
    }

    private static FiscalExceptionSemanticHashRecalculationPreviewAuditRecord MapRecord(NpgsqlDataReader reader) =>
        new(
            RecalculationPreviewAuditId: reader.GetGuid(
                reader.GetOrdinal("semantic_hash_recalculation_preview_audit_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            StoredSemanticHashSourceVersion: GetNullableString(reader, "stored_semantic_hash_source_version"),
            RequiredSemanticHashSourceVersion: reader.GetString(
                reader.GetOrdinal("required_semantic_hash_source_version")),
            StoredSemanticHashValue: GetNullableString(reader, "stored_semantic_hash_value"),
            PreviewStatus: ParsePreviewStatus(reader.GetString(reader.GetOrdinal("recalculation_preview_status"))),
            BlockReasonCode: GetNullableString(reader, "recalculation_block_reason_code"),
            CompleteOriginalRequestFactsAvailable: reader.GetBoolean(
                reader.GetOrdinal("complete_original_request_facts_available")),
            RecalculatedHashValue: GetNullableString(reader, "recalculated_hash_value"),
            RecalculatedHashAlgorithm: GetNullableString(reader, "recalculated_hash_algorithm"),
            RecalculatedHashSourceVersion: GetNullableString(reader, "recalculated_hash_source_version"),
            RecalculatedSourceFactCount: GetNullableInt32(reader, "recalculated_source_fact_count"),
            RecalculatedSafeSourceSummary: GetNullableString(reader, "safe_source_summary"),
            RecalculatedHashMatchesStoredHash: GetNullableBoolean(reader, "recalculated_hash_matches_stored"),
            MutationStatus: ParseMutationStatus(reader.GetString(reader.GetOrdinal("mutation_status"))),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            ServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));

    private static string ToStorageValue(FiscalExceptionSemanticHashRecalculationPreviewStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashRecalculationPreviewStatus.NotRequired => "NOT_REQUIRED",
            FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated => "PREVIEW_CALCULATED",
            FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashRecalculationPreviewStatus.Unavailable => "UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown recalculation preview status.")
        };

    private static FiscalExceptionSemanticHashRecalculationPreviewStatus ParsePreviewStatus(string value) =>
        value switch
        {
            "NOT_REQUIRED" => FiscalExceptionSemanticHashRecalculationPreviewStatus.NotRequired,
            "PREVIEW_CALCULATED" => FiscalExceptionSemanticHashRecalculationPreviewStatus.PreviewCalculated,
            "BLOCKED" => FiscalExceptionSemanticHashRecalculationPreviewStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionSemanticHashRecalculationPreviewStatus.Unavailable,
            _ => FiscalExceptionSemanticHashRecalculationPreviewStatus.Unavailable
        };

    private static string ToStorageValue(FiscalExceptionSemanticHashRecalculationMutationStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated => "NOT_MUTATED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown recalculation mutation status.")
        };

    private static FiscalExceptionSemanticHashRecalculationMutationStatus ParseMutationStatus(string value) =>
        value switch
        {
            "NOT_MUTATED" => FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated,
            _ => FiscalExceptionSemanticHashRecalculationMutationStatus.NotMutated
        };

    private static void AddNullable<T>(NpgsqlCommand command, string name, T? value)
    {
        if (value is not null)
        {
            command.Parameters.AddWithValue(name, value);
            return;
        }

        var parameter = TryGetNpgsqlDbType(typeof(T), out var dbType)
            ? command.Parameters.Add(name, dbType)
            : command.Parameters.AddWithValue(name, DBNull.Value);

        parameter.Value = DBNull.Value;
    }

    private static bool TryGetNpgsqlDbType(Type type, out NpgsqlDbType dbType)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType == typeof(Guid))
        {
            dbType = NpgsqlDbType.Uuid;
            return true;
        }

        if (underlyingType == typeof(string))
        {
            dbType = NpgsqlDbType.Text;
            return true;
        }

        if (underlyingType == typeof(int))
        {
            dbType = NpgsqlDbType.Integer;
            return true;
        }

        if (underlyingType == typeof(bool))
        {
            dbType = NpgsqlDbType.Boolean;
            return true;
        }

        dbType = default;
        return false;
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static int? GetNullableInt32(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static bool? GetNullableBoolean(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? TruncateOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
