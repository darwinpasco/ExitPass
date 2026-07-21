using System.Data;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

/// <summary>
/// PostgreSQL-backed shared statutory-discount command/readback repository.
/// </summary>
public sealed class PostgresStatutoryDiscountDecisionFacadeRepository
    : IStatutoryDiscountDecisionFacadeRepository
{
    private readonly string _connectionString;

    public PostgresStatutoryDiscountDecisionFacadeRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<T> ExecuteWithCommandLockAsync<T>(
        StatutoryDiscountDecisionRepositoryCommand command,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await AcquireSessionLockAsync(connection, command.IdempotencyScope, cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseSessionLockAsync(connection, command.IdempotencyScope, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    public async Task<StatutoryDiscountDecisionBeginResult> BeginAsync(
        StatutoryDiscountDecisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await ReadByIdempotencyAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false);
            existing ??= await ReadByBusinessIdentityAsync(
                connection,
                transaction,
                command,
                cancellationToken).ConfigureAwait(false);
            existing ??= await ReadByRequestReferenceAsync(
                connection,
                transaction,
                command.Command.RequestReference,
                cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new StatutoryDiscountDecisionBeginResult(
                    Existing: true,
                    SemanticConflict: !string.Equals(existing.SemanticRequestHash, command.SemanticRequestHash, StringComparison.Ordinal),
                    existing);
            }

            var created = await InsertAsync(connection, transaction, command, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken);
            return new StatutoryDiscountDecisionBeginResult(Existing: false, SemanticConflict: false, created);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<StatutoryDiscountDecisionCommandRecord> CompleteAsync(
        StatutoryDiscountDecisionCommandRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE discounts.statutory_discount_decision_commands
               SET statutory_discount_validation_id = @statutory_discount_validation_id,
                   payable_basis_application_id = @payable_basis_application_id,
                   original_tariff_snapshot_id = @original_tariff_snapshot_id,
                   applied_tariff_snapshot_id = @applied_tariff_snapshot_id,
                   decision_status = @decision_status,
                   result_classification = @result_classification,
                   policy_resolution_basis = @policy_resolution_basis,
                   applied_policy_reference_id = @applied_policy_reference_id,
                   fallback_policy_reference_id = @fallback_policy_reference_id,
                   local_ordinance_applied = @local_ordinance_applied,
                   gross_amount_minor_units = @gross_amount_minor_units,
                   statutory_discount_amount_minor_units = @statutory_discount_amount_minor_units,
                   net_payable_amount_minor_units = @net_payable_amount_minor_units,
                   currency_code = @currency_code,
                   evidence_required = @evidence_required,
                   evidence_recorded = @evidence_recorded,
                   reason_code = @reason_code,
                   error_code = @error_code,
                   decided_at = @decided_at,
                   applied_at = @applied_at,
                   completed_at = CASE
                       WHEN @decision_status <> 'PROCESSING' THEN COALESCE(completed_at, now())
                       ELSE completed_at
                   END,
                   updated_at = now()
             WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id
             RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddCompletionParameters(dbCommand, record);
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new StatutoryDiscountDecisionRejectedException(
                "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                "Statutory discount decision command was not found.",
                isNotFound: true);
        }

        return ReadRecord(reader);
    }

    public async Task<StatutoryDiscountDecisionCommandRecord?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) with { CorrelationId = correlationId } : null;
    }

    private static async Task AcquireSessionLockAsync(
        NpgsqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_lock(hashtextextended(@lock_key, 0));";

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("lock_key", NpgsqlDbType.Text).Value = lockKey;
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReleaseSessionLockAsync(
        NpgsqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_unlock(hashtextextended(@lock_key, 0));";

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("lock_key", NpgsqlDbType.Text).Value = lockKey;
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<StatutoryDiscountDecisionCommandRecord?> ReadByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountDecisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE idempotency_scope = @idempotency_scope
              AND idempotency_key = @idempotency_key
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionCommandRecord?> ReadByRequestReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid requestReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE request_reference = @request_reference
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = requestReference;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionCommandRecord?> ReadByBusinessIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountDecisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type = @entitlement_type
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.Command.ParkingSessionId;
        dbCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Varchar).Value = command.Command.EntitlementType;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionCommandRecord> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountDecisionRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_decision_commands (
                request_reference,
                parking_session_id,
                source_channel,
                entitlement_type,
                idempotency_scope,
                idempotency_key,
                semantic_request_hash,
                semantic_hash_source_version,
                decision_status,
                result_classification,
                evidence_required,
                evidence_recorded,
                original_correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @request_reference,
                @parking_session_id,
                @source_channel,
                @entitlement_type,
                @idempotency_scope,
                @idempotency_key,
                @semantic_request_hash,
                @semantic_hash_source_version,
                'PROCESSING',
                'ACCEPTED',
                @evidence_required,
                false,
                @correlation_id,
                @now,
                @now
            )
            RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = command.Command.RequestReference;
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.Command.ParkingSessionId;
        dbCommand.Parameters.Add("source_channel", NpgsqlDbType.Varchar).Value = command.Command.SourceChannel;
        dbCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Varchar).Value = command.Command.EntitlementType;
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        dbCommand.Parameters.Add("semantic_request_hash", NpgsqlDbType.Varchar).Value = command.SemanticRequestHash;
        dbCommand.Parameters.Add("semantic_hash_source_version", NpgsqlDbType.Varchar).Value = command.SemanticHashSourceVersion;
        dbCommand.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value =
            command.Command.EvidenceCaptureRequested || command.Command.EvidenceReferences.Count > 0;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.Command.CorrelationId;
        dbCommand.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = command.RequestedAt;

        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Statutory discount decision command insert returned no row.");
        }

        return ReadRecord(reader);
    }

    private static void AddCompletionParameters(NpgsqlCommand dbCommand, StatutoryDiscountDecisionCommandRecord record)
    {
        dbCommand.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            record.StatutoryDiscountDecisionCommandId;
        AddNullable(dbCommand, "statutory_discount_validation_id", NpgsqlDbType.Uuid, record.StatutoryDiscountValidationId);
        AddNullable(dbCommand, "payable_basis_application_id", NpgsqlDbType.Uuid, record.PayableBasisApplicationId);
        AddNullable(dbCommand, "original_tariff_snapshot_id", NpgsqlDbType.Uuid, record.OriginalTariffSnapshotId);
        AddNullable(dbCommand, "applied_tariff_snapshot_id", NpgsqlDbType.Uuid, record.AppliedTariffSnapshotId);
        dbCommand.Parameters.Add("decision_status", NpgsqlDbType.Varchar).Value = record.DecisionStatus;
        dbCommand.Parameters.Add("result_classification", NpgsqlDbType.Varchar).Value = record.ResultClassification;
        AddNullable(dbCommand, "policy_resolution_basis", NpgsqlDbType.Varchar, record.PolicyResolutionBasis);
        AddNullable(dbCommand, "applied_policy_reference_id", NpgsqlDbType.Uuid, record.AppliedPolicyReferenceId);
        AddNullable(dbCommand, "fallback_policy_reference_id", NpgsqlDbType.Uuid, record.FallbackPolicyReferenceId);
        dbCommand.Parameters.Add("local_ordinance_applied", NpgsqlDbType.Boolean).Value = record.LocalOrdinanceApplied;
        AddNullable(dbCommand, "gross_amount_minor_units", NpgsqlDbType.Bigint, record.GrossAmountMinorUnits);
        AddNullable(dbCommand, "statutory_discount_amount_minor_units", NpgsqlDbType.Bigint, record.StatutoryDiscountAmountMinorUnits);
        AddNullable(dbCommand, "net_payable_amount_minor_units", NpgsqlDbType.Bigint, record.NetPayableAmountMinorUnits);
        AddNullable(dbCommand, "currency_code", NpgsqlDbType.Char, record.Currency);
        dbCommand.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value = record.EvidenceRequired;
        dbCommand.Parameters.Add("evidence_recorded", NpgsqlDbType.Boolean).Value = record.EvidenceRecorded;
        AddNullable(dbCommand, "reason_code", NpgsqlDbType.Varchar, record.ReasonCode);
        AddNullable(dbCommand, "error_code", NpgsqlDbType.Varchar, record.ErrorCode);
        AddNullable(dbCommand, "decided_at", NpgsqlDbType.TimestampTz, record.DecidedAt);
        AddNullable(dbCommand, "applied_at", NpgsqlDbType.TimestampTz, record.AppliedAt);
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    private static StatutoryDiscountDecisionCommandRecord ReadRecord(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("request_reference")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("source_channel")),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetString(reader.GetOrdinal("idempotency_key")),
            reader.GetString(reader.GetOrdinal("decision_status")),
            reader.GetString(reader.GetOrdinal("result_classification")),
            reader.GetString(reader.GetOrdinal("idempotency_scope")),
            reader.GetString(reader.GetOrdinal("semantic_hash_source_version")),
            reader.GetString(reader.GetOrdinal("semantic_request_hash")),
            GetNullableGuid(reader, "statutory_discount_validation_id"),
            GetNullableGuid(reader, "payable_basis_application_id"),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            GetNullableGuid(reader, "applied_tariff_snapshot_id"),
            GetNullableString(reader, "policy_resolution_basis"),
            GetNullableGuid(reader, "applied_policy_reference_id"),
            GetNullableGuid(reader, "fallback_policy_reference_id"),
            reader.GetBoolean(reader.GetOrdinal("local_ordinance_applied")),
            GetNullableInt64(reader, "gross_amount_minor_units"),
            GetNullableInt64(reader, "statutory_discount_amount_minor_units"),
            GetNullableInt64(reader, "net_payable_amount_minor_units"),
            GetNullableString(reader, "currency_code")?.Trim(),
            reader.GetBoolean(reader.GetOrdinal("evidence_required")),
            reader.GetBoolean(reader.GetOrdinal("evidence_recorded")),
            GetNullableString(reader, "reason_code"),
            GetNullableString(reader, "error_code"),
            reader.GetGuid(reader.GetOrdinal("original_correlation_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            GetNullableDateTimeOffset(reader, "decided_at"),
            GetNullableDateTimeOffset(reader, "applied_at"));

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

    private static long? GetNullableInt64(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
