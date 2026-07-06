using ExitPass.CentralPms.Application.FiscalIssuance;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class PostgresFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository :
    IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository
{
    private readonly string _connectionString;

    public PostgresFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt,
        CancellationToken cancellationToken)
    {
        Validate(attempt);

        const string sql = """
            INSERT INTO core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                controlled_backfill_approval_status,
                old_semantic_hash_source_version,
                required_semantic_hash_source_version,
                old_semantic_hash_value,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference
            )
            VALUES (
                @fiscal_issuance_reference_id,
                @semantic_hash_recalculation_preview_audit_id,
                @controlled_backfill_approval_status,
                @old_semantic_hash_source_version,
                @required_semantic_hash_source_version,
                @old_semantic_hash_value,
                @new_semantic_hash_value,
                @new_semantic_hash_algorithm,
                @new_semantic_hash_source_version,
                @new_semantic_hash_source_fact_count,
                @safe_source_summary,
                @mutation_preparation_status,
                @mutation_block_reason_code,
                @mutation_mode,
                @mutation_enabled,
                @fiscal_issuance_reference_mutated,
                @attempted_at,
                @safe_summary,
                @correlation_id,
                @actor_service_identity_id,
                @approval_reference,
                @dual_control_reference
            )
            RETURNING
                semantic_hash_backfill_mutation_audit_id,
                fiscal_issuance_reference_id,
                semantic_hash_recalculation_preview_audit_id,
                controlled_backfill_approval_status,
                old_semantic_hash_source_version,
                required_semantic_hash_source_version,
                old_semantic_hash_value,
                new_semantic_hash_value,
                new_semantic_hash_algorithm,
                new_semantic_hash_source_version,
                new_semantic_hash_source_fact_count,
                safe_source_summary,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                correlation_id,
                actor_service_identity_id,
                approval_reference,
                dual_control_reference,
                created_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        AddParameters(command, attempt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Semantic hash controlled backfill mutation audit insert returned no rows.");
        }

        return MapRecord(reader);
    }

    public async Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?> GetSummaryAsync(
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
                semantic_hash_backfill_mutation_audit_id,
                mutation_preparation_status,
                mutation_block_reason_code,
                mutation_mode,
                mutation_enabled,
                fiscal_issuance_reference_mutated,
                attempted_at,
                safe_summary,
                COUNT(*) OVER ()::integer AS attempt_count
            FROM core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
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

        return new FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary(
            LastMutationAuditId: reader.GetGuid(reader.GetOrdinal("semantic_hash_backfill_mutation_audit_id")),
            LastMutationStatus: ParseMutationStatus(reader.GetString(reader.GetOrdinal("mutation_preparation_status"))),
            LastAttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            AttemptCount: reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastBlockReasonCode: GetNullableString(reader, "mutation_block_reason_code"),
            MutationMode: ParseMutationMode(reader.GetString(reader.GetOrdinal("mutation_mode"))),
            MutationEnabled: reader.GetBoolean(reader.GetOrdinal("mutation_enabled")),
            FiscalIssuanceReferenceMutated: reader.GetBoolean(reader.GetOrdinal("fiscal_issuance_reference_mutated")),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")));
    }

    private static void AddParameters(
        NpgsqlCommand command,
        FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt)
    {
        command.Parameters.AddWithValue("fiscal_issuance_reference_id", attempt.FiscalIssuanceReferenceId);
        AddNullable(command, "semantic_hash_recalculation_preview_audit_id", attempt.RecalculationPreviewAuditId);
        command.Parameters.AddWithValue(
            "controlled_backfill_approval_status",
            ToStorageValue(attempt.ApprovalBasisStatus));
        AddNullable(command, "old_semantic_hash_source_version", TruncateOrNull(attempt.OldSourceVersion, 80));
        command.Parameters.AddWithValue(
            "required_semantic_hash_source_version",
            Truncate(attempt.RequiredSourceVersion, 80));
        AddNullable(command, "old_semantic_hash_value", TruncateOrNull(attempt.OldHashValue, 64));
        AddNullable(command, "new_semantic_hash_value", TruncateOrNull(attempt.NewHashValue, 64));
        AddNullable(command, "new_semantic_hash_algorithm", TruncateOrNull(attempt.NewHashAlgorithm, 32));
        AddNullable(command, "new_semantic_hash_source_version", TruncateOrNull(attempt.NewHashSourceVersion, 80));
        AddNullable(command, "new_semantic_hash_source_fact_count", attempt.NewHashSourceFactCount);
        AddNullable(command, "safe_source_summary", TruncateOrNull(attempt.SafeSourceSummary, 240));
        command.Parameters.AddWithValue("mutation_preparation_status", ToStorageValue(attempt.MutationStatus));
        AddNullable(command, "mutation_block_reason_code", TruncateOrNull(attempt.BlockReasonCode, 160));
        command.Parameters.AddWithValue("mutation_mode", ToStorageValue(attempt.MutationMode));
        command.Parameters.AddWithValue("mutation_enabled", attempt.MutationEnabled);
        command.Parameters.AddWithValue(
            "fiscal_issuance_reference_mutated",
            attempt.FiscalIssuanceReferenceMutated);
        command.Parameters.AddWithValue("attempted_at", attempt.AttemptedAt);
        command.Parameters.AddWithValue("safe_summary", Truncate(attempt.SafeSummary, 240));
        AddNullable(command, "correlation_id", attempt.CorrelationId);
        AddNullable(command, "actor_service_identity_id", attempt.ActorServiceIdentityId);
        AddNullable(command, "approval_reference", TruncateOrNull(attempt.ApprovalReference, 160));
        AddNullable(command, "dual_control_reference", TruncateOrNull(attempt.DualControlReference, 160));
    }

    private static void Validate(FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt)
    {
        if (attempt.FiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.RequiredSourceVersion))
        {
            throw new ArgumentException("Required semantic hash source version is required.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.SafeSummary))
        {
            throw new ArgumentException("Semantic hash backfill mutation safe summary is required.", nameof(attempt));
        }

        if (attempt.FiscalIssuanceReferenceMutated)
        {
            throw new ArgumentException(
                "This slice cannot persist a mutated fiscal issuance reference audit record.",
                nameof(attempt));
        }
    }

    private static FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord MapRecord(NpgsqlDataReader reader) =>
        new(
            MutationAuditId: reader.GetGuid(reader.GetOrdinal("semantic_hash_backfill_mutation_audit_id")),
            FiscalIssuanceReferenceId: reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            RecalculationPreviewAuditId: GetNullableGuid(reader, "semantic_hash_recalculation_preview_audit_id"),
            ApprovalBasisStatus: ParseApprovalStatus(
                reader.GetString(reader.GetOrdinal("controlled_backfill_approval_status"))),
            OldSourceVersion: GetNullableString(reader, "old_semantic_hash_source_version"),
            RequiredSourceVersion: reader.GetString(reader.GetOrdinal("required_semantic_hash_source_version")),
            OldHashValue: GetNullableString(reader, "old_semantic_hash_value"),
            NewHashValue: GetNullableString(reader, "new_semantic_hash_value"),
            NewHashAlgorithm: GetNullableString(reader, "new_semantic_hash_algorithm"),
            NewHashSourceVersion: GetNullableString(reader, "new_semantic_hash_source_version"),
            NewHashSourceFactCount: GetNullableInt32(reader, "new_semantic_hash_source_fact_count"),
            SafeSourceSummary: GetNullableString(reader, "safe_source_summary"),
            MutationStatus: ParseMutationStatus(reader.GetString(reader.GetOrdinal("mutation_preparation_status"))),
            BlockReasonCode: GetNullableString(reader, "mutation_block_reason_code"),
            MutationMode: ParseMutationMode(reader.GetString(reader.GetOrdinal("mutation_mode"))),
            MutationEnabled: reader.GetBoolean(reader.GetOrdinal("mutation_enabled")),
            FiscalIssuanceReferenceMutated: reader.GetBoolean(
                reader.GetOrdinal("fiscal_issuance_reference_mutated")),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            SafeSummary: reader.GetString(reader.GetOrdinal("safe_summary")),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            ActorServiceIdentityId: GetNullableGuid(reader, "actor_service_identity_id"),
            ApprovalReference: GetNullableString(reader, "approval_reference"),
            DualControlReference: GetNullableString(reader, "dual_control_reference"),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));

    private static string ToStorageValue(FiscalExceptionSemanticHashControlledBackfillApprovalStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.NotRequiredCurrent => "NOT_REQUIRED_CURRENT",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill => "READY_FOR_CONTROLLED_BACKFILL",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl => "PENDING_DUAL_CONTROL",
            FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Unavailable => "UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown approval status.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillApprovalStatus ParseApprovalStatus(string value) =>
        value switch
        {
            "NOT_REQUIRED_CURRENT" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.NotRequiredCurrent,
            "READY_FOR_CONTROLLED_BACKFILL" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.ReadyForControlledBackfill,
            "PENDING_DUAL_CONTROL" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.PendingDualControl,
            "UNAVAILABLE" => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Unavailable,
            _ => FiscalExceptionSemanticHashControlledBackfillApprovalStatus.Blocked
        };

    private static string ToStorageValue(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus status) =>
        status switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.NotPrepared => "NOT_PREPARED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled => "PREPARED_BUT_MUTATION_DISABLED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked => "BLOCKED",
            FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable => "UNAVAILABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown mutation status.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus ParseMutationStatus(
        string value) =>
        value switch
        {
            "PREPARED_BUT_MUTATION_DISABLED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.PreparedButMutationDisabled,
            "BLOCKED" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Blocked,
            "UNAVAILABLE" => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.Unavailable,
            _ => FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus.NotPrepared
        };

    private static string ToStorageValue(FiscalExceptionSemanticHashControlledBackfillMutationMode mode) =>
        mode switch
        {
            FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly => "SINGLE_RECORD_ONLY",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mutation mode.")
        };

    private static FiscalExceptionSemanticHashControlledBackfillMutationMode ParseMutationMode(string value) =>
        value switch
        {
            "SINGLE_RECORD_ONLY" => FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly,
            _ => FiscalExceptionSemanticHashControlledBackfillMutationMode.SingleRecordOnly
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

        dbType = default;
        return false;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Truncate(value.Trim(), maxLength);

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
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
}
