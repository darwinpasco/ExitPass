using System.Data;
using System.Data.Common;
using ExitPass.CentralPms.Application.Gates;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// PostgreSQL-backed read-only repository for canonical gate command state inventory.
/// </summary>
public sealed class GateCommandStateReadRepository : IGateCommandStateReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a gate command state read repository.
    /// </summary>
    public GateCommandStateReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateCommandStateReadModel?> GetByConsumptionIdAsync(
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken)
    {
        if (gateAuthorizationConsumptionId == Guid.Empty)
        {
            throw new ArgumentException("Gate authorization consumption id is required.", nameof(gateAuthorizationConsumptionId));
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var consumption = await ReadConsumptionAsync(connection, gateAuthorizationConsumptionId, cancellationToken);
        if (consumption is null)
        {
            return null;
        }

        var processing = await ReadProcessingAsync(connection, gateAuthorizationConsumptionId, cancellationToken);
        var command = await ReadCommandAsync(connection, gateAuthorizationConsumptionId, cancellationToken);
        var attempts = await ReadHikCentralAttemptsAsync(connection, gateAuthorizationConsumptionId, cancellationToken);

        return new GateCommandStateReadModel(consumption, processing, command, attempts);
    }

    private static async Task<GateAuthorizationConsumptionReadModel?> ReadConsumptionAsync(
        NpgsqlConnection connection,
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                gate_authorization_consumption_id,
                exit_authorization_id,
                gate_device_id,
                site_id,
                lane_id,
                consume_status::text AS consume_status,
                consumed_at,
                correlation_id
            FROM gates.gate_authorization_consumptions
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = gateAuthorizationConsumptionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new GateAuthorizationConsumptionReadModel(
            reader.GetGuid("gate_authorization_consumption_id"),
            GetNullableGuid(reader, "exit_authorization_id"),
            GetNullableGuid(reader, "gate_device_id"),
            reader.GetGuid("site_id"),
            GetNullableGuid(reader, "lane_id"),
            reader.GetString("consume_status"),
            GetNullableDateTimeOffset(reader, "consumed_at"),
            GetNullableGuid(reader, "correlation_id"));
    }

    private static async Task<GateAuthorizationConsumedProcessingReadModel?> ReadProcessingAsync(
        NpgsqlConnection connection,
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                processing_id,
                processing_key,
                event_id,
                event_type,
                processing_status,
                processing_result,
                attempt_count,
                first_attempted_at,
                last_attempted_at,
                processed_at,
                failure_code,
                failure_reason
            FROM gates.gate_authorization_consumed_processing
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
            ORDER BY created_at DESC, processing_id DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = gateAuthorizationConsumptionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new GateAuthorizationConsumedProcessingReadModel(
            reader.GetGuid("processing_id"),
            reader.GetGuid("processing_key"),
            GetNullableGuid(reader, "event_id"),
            reader.GetString("event_type"),
            reader.GetString("processing_status"),
            reader.GetString("processing_result"),
            reader.GetInt32("attempt_count"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("first_attempted_at")),
            GetNullableDateTimeOffset(reader, "last_attempted_at"),
            GetNullableDateTimeOffset(reader, "processed_at"),
            GetNullableString(reader, "failure_code"),
            GetNullableString(reader, "failure_reason"));
    }

    private static async Task<GateCommandReadModel?> ReadCommandAsync(
        NpgsqlConnection connection,
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                command_id,
                command_type,
                command_status,
                attempt_count,
                max_attempts,
                retry_policy_code,
                requested_at,
                started_at,
                last_attempted_at,
                next_attempt_at,
                completed_at,
                terminal_failure_at,
                failure_code,
                failure_reason,
                last_failure_code,
                last_failure_reason
            FROM gates.gate_commands
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
            ORDER BY requested_at DESC, command_id DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = gateAuthorizationConsumptionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new GateCommandReadModel(
            reader.GetGuid("command_id"),
            reader.GetString("command_type"),
            reader.GetString("command_status"),
            reader.GetInt32("attempt_count"),
            reader.GetInt32("max_attempts"),
            reader.GetString("retry_policy_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableDateTimeOffset(reader, "started_at"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            GetNullableDateTimeOffset(reader, "next_attempt_at"),
            GetNullableDateTimeOffset(reader, "completed_at"),
            GetNullableDateTimeOffset(reader, "terminal_failure_at"),
            GetNullableString(reader, "failure_code"),
            GetNullableString(reader, "failure_reason"),
            GetNullableString(reader, "last_failure_code"),
            GetNullableString(reader, "last_failure_reason"));
    }

    private static async Task<IReadOnlyList<HikCentralGateActionAuditReadModel>> ReadHikCentralAttemptsAsync(
        NpgsqlConnection connection,
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                hikcentral_gate_action_audit_id,
                vendor_code,
                vendor_operation,
                door_index_code,
                request_method,
                request_path,
                request_hash,
                signed_header_names,
                request_correlation_id,
                vendor_correlation_id,
                http_status_code,
                vendor_result_code,
                vendor_result_message,
                action_outcome,
                retryable,
                failure_recorded,
                duration_ms,
                timed_out,
                vendor_unavailable,
                transport_failure,
                requested_at,
                responded_at
            FROM gates.hikcentral_gate_action_audits
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
            ORDER BY requested_at DESC, hikcentral_gate_action_audit_id DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = gateAuthorizationConsumptionId;

        var attempts = new List<HikCentralGateActionAuditReadModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(new HikCentralGateActionAuditReadModel(
                reader.GetGuid("hikcentral_gate_action_audit_id"),
                reader.GetString("vendor_code"),
                reader.GetString("vendor_operation"),
                reader.GetString("door_index_code"),
                reader.GetString("request_method"),
                reader.GetString("request_path"),
                reader.GetString("request_hash"),
                reader.GetString("signed_header_names"),
                reader.GetGuid("request_correlation_id"),
                GetNullableString(reader, "vendor_correlation_id"),
                GetNullableInt32(reader, "http_status_code"),
                GetNullableString(reader, "vendor_result_code"),
                GetNullableString(reader, "vendor_result_message"),
                reader.GetString("action_outcome"),
                reader.GetBoolean("retryable"),
                reader.GetBoolean("failure_recorded"),
                reader.GetInt32("duration_ms"),
                reader.GetBoolean("timed_out"),
                reader.GetBoolean("vendor_unavailable"),
                reader.GetBoolean("transport_failure"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("responded_at"))));
        }

        return attempts;
    }

    private static Guid? GetNullableGuid(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
