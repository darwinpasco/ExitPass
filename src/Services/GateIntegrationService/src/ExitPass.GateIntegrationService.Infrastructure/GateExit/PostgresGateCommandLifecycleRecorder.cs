using System.Data;
using ExitPass.GateIntegrationService.Application.GateExit;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// PostgreSQL-backed internal vendor-neutral gate command lifecycle recorder.
/// </summary>
public sealed class PostgresGateCommandLifecycleRecorder : IGateCommandLifecycleRecorder
{
    private const string CommandType = "GateAuthorizationConsumed";
    private readonly string _connectionString;
    private readonly GateCommandRetryPolicy _retryPolicy = GateCommandRetryPolicy.Default;

    /// <summary>
    /// Creates a durable command lifecycle recorder using the configured main database.
    /// </summary>
    public PostgresGateCommandLifecycleRecorder(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <inheritdoc />
    public async Task<GateCommandLifecycleStart> BeginCommandAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        var now = DateTimeOffset.UtcNow;
        var processingKey = ResolveProcessingKey(handoff);

        const string sql = """
            INSERT INTO gates.gate_commands (
                command_id,
                command_type,
                source_processing_id,
                source_event_id,
                exit_authorization_id,
                gate_authorization_consumption_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                gate_device_identifier,
                lane_id,
                site_id,
                vendor_system_id,
                command_status,
                attempt_count,
                max_attempts,
                retry_policy_code,
                requested_at,
                last_attempted_at,
                started_at,
                completed_at,
                next_attempt_at,
                terminal_failure_at,
                failure_code,
                failure_reason,
                last_failure_code,
                last_failure_reason,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @command_id,
                @command_type,
                @source_processing_id,
                @source_event_id,
                @exit_authorization_id,
                @gate_authorization_consumption_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @gate_device_identifier,
                @lane_id,
                @site_id,
                @vendor_system_id,
                'IN_PROGRESS',
                1,
                @max_attempts,
                @retry_policy_code,
                @now,
                @now,
                @now,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                @correlation_id,
                @now,
                @now
            )
            ON CONFLICT (source_processing_id, command_type) DO UPDATE
            SET
                command_status = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN 'IN_PROGRESS'
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count >= gates.gate_commands.max_attempts
                        THEN 'TERMINAL_FAILURE'
                    ELSE gates.gate_commands.command_status
                END,
                attempt_count = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN LEAST(gates.gate_commands.attempt_count + 1, gates.gate_commands.max_attempts)
                    ELSE gates.gate_commands.attempt_count
                END,
                last_attempted_at = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN @now
                    ELSE gates.gate_commands.last_attempted_at
                END,
                started_at = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN @now
                    ELSE gates.gate_commands.started_at
                END,
                completed_at = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN NULL
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count >= gates.gate_commands.max_attempts
                        THEN COALESCE(gates.gate_commands.completed_at, @now)
                    ELSE gates.gate_commands.completed_at
                END,
                next_attempt_at = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN NULL
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count >= gates.gate_commands.max_attempts
                        THEN NULL
                    ELSE gates.gate_commands.next_attempt_at
                END,
                terminal_failure_at = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count >= gates.gate_commands.max_attempts
                        THEN COALESCE(gates.gate_commands.terminal_failure_at, @now)
                    ELSE gates.gate_commands.terminal_failure_at
                END,
                failure_code = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN NULL
                    ELSE gates.gate_commands.failure_code
                END,
                failure_reason = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                         AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                        THEN NULL
                    ELSE gates.gate_commands.failure_reason
                END,
                updated_at = CASE
                    WHEN gates.gate_commands.command_status IN ('FAILED', 'RETRYABLE')
                         AND (
                             gates.gate_commands.attempt_count < gates.gate_commands.max_attempts
                             AND COALESCE(gates.gate_commands.next_attempt_at, @now) <= @now
                             OR gates.gate_commands.attempt_count >= gates.gate_commands.max_attempts
                         )
                        THEN @now
                    ELSE gates.gate_commands.updated_at
                END
            RETURNING
                command_id,
                source_processing_id,
                source_event_id,
                exit_authorization_id,
                gate_authorization_consumption_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                gate_device_identifier,
                lane_id,
                site_id,
                vendor_system_id,
                command_status,
                attempt_count,
                max_attempts,
                retry_policy_code,
                requested_at,
                last_attempted_at,
                started_at,
                completed_at,
                next_attempt_at,
                terminal_failure_at,
                failure_code,
                failure_reason,
                last_failure_code,
                last_failure_reason,
                correlation_id,
                (xmax = 0) AS inserted;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        AddHandoffParameters(command, handoff, processingKey, now);

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Gate command lifecycle state was not returned.");
        }

        var record = ReadRecord(reader);
        var inserted = reader.GetBoolean(reader.GetOrdinal("inserted"));
        var canInvokeAdapter = record.CommandStatus == GateCommandStatus.InProgress
            && (inserted || record.AttemptCount > 1);

        return new GateCommandLifecycleStart(record, inserted, canInvokeAdapter);
    }

    /// <inheritdoc />
    public async Task RecordSucceededAsync(
        Guid commandId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = 'SUCCEEDED',
                completed_at = @completed_at,
                next_attempt_at = NULL,
                terminal_failure_at = NULL,
                failure_code = NULL,
                failure_reason = NULL,
                last_failure_code = NULL,
                last_failure_reason = NULL,
                updated_at = @completed_at
            WHERE command_id = @command_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
        command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = completedAtUtc;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordFailedAsync(
        Guid commandId,
        string failureCode,
        string failureReason,
        bool retryable,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = CASE
                    WHEN @retryable AND attempt_count >= max_attempts THEN 'TERMINAL_FAILURE'
                    WHEN @retryable THEN 'RETRYABLE'
                    ELSE 'FAILED'
                END,
                completed_at = @completed_at,
                next_attempt_at = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN @next_attempt_at
                    ELSE NULL
                END,
                terminal_failure_at = CASE
                    WHEN @retryable AND attempt_count >= max_attempts THEN @completed_at
                    ELSE terminal_failure_at
                END,
                failure_code = @failure_code,
                failure_reason = @failure_reason,
                last_failure_code = @failure_code,
                last_failure_reason = @failure_reason,
                updated_at = @completed_at
            WHERE command_id = @command_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
        var completedAtUtc = DateTimeOffset.UtcNow;
        command.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = retryable;
        command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = completedAtUtc;
        command.Parameters.Add("next_attempt_at", NpgsqlDbType.TimestampTz).Value = completedAtUtc.Add(_retryPolicy.RetryDelay);
        command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value = failureCode;
        command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value = failureReason;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddHandoffParameters(
        NpgsqlCommand command,
        GateAuthorizationConsumedHandoff handoff,
        Guid processingKey,
        DateTimeOffset now)
    {
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("command_type", NpgsqlDbType.Varchar).Value = CommandType;
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = processingKey;
        command.Parameters.Add("source_event_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.EventId == Guid.Empty ? null : handoff.EventId);
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = handoff.ExitAuthorizationId;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = handoff.GateAuthorizationConsumptionId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = handoff.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = handoff.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = handoff.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.GateDeviceId);
        command.Parameters.Add("gate_device_identifier", NpgsqlDbType.Varchar).Value = DbValue(handoff.GateDeviceIdentifier);
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.LaneId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.SiteId);
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.VendorSystemId);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = handoff.CorrelationId;
        command.Parameters.Add("max_attempts", NpgsqlDbType.Integer).Value = GateCommandRetryPolicy.Default.MaxAttempts;
        command.Parameters.Add("retry_policy_code", NpgsqlDbType.Varchar).Value = GateCommandRetryPolicy.Default.PolicyCode;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
    }

    private static GateCommandLifecycleRecord ReadRecord(NpgsqlDataReader reader)
    {
        return new GateCommandLifecycleRecord(
            reader.GetGuid(reader.GetOrdinal("command_id")),
            reader.GetGuid(reader.GetOrdinal("source_processing_id")),
            ReadNullableGuid(reader, "source_event_id") ?? Guid.Empty,
            reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            ReadNullableGuid(reader, "gate_device_id"),
            ReadNullableString(reader, "gate_device_identifier"),
            ReadNullableGuid(reader, "lane_id"),
            ReadNullableGuid(reader, "site_id"),
            ReadNullableGuid(reader, "vendor_system_id"),
            ParseStatus(reader.GetString(reader.GetOrdinal("command_status"))),
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            reader.GetInt32(reader.GetOrdinal("max_attempts")),
            reader.GetString(reader.GetOrdinal("retry_policy_code")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            ReadNullableDateTimeOffset(reader, "started_at"),
            ReadNullableDateTimeOffset(reader, "completed_at"),
            ReadNullableDateTimeOffset(reader, "next_attempt_at"),
            ReadNullableDateTimeOffset(reader, "terminal_failure_at"),
            ReadNullableString(reader, "failure_code"),
            ReadNullableString(reader, "failure_reason"),
            ReadNullableString(reader, "last_failure_code"),
            ReadNullableString(reader, "last_failure_reason"),
            reader.GetGuid(reader.GetOrdinal("correlation_id")));
    }

    private static GateCommandStatus ParseStatus(string status) =>
        status switch
        {
            "REQUESTED" => GateCommandStatus.Requested,
            "IN_PROGRESS" => GateCommandStatus.InProgress,
            "SUCCEEDED" => GateCommandStatus.Succeeded,
            "FAILED" => GateCommandStatus.Failed,
            "RETRYABLE" => GateCommandStatus.Retryable,
            "TERMINAL_FAILURE" => GateCommandStatus.TerminalFailure,
            _ => throw new InvalidOperationException($"Unknown gate command status '{status}'.")
        };

    private static string? ReadNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? ReadNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static Guid ResolveProcessingKey(GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value.Value : DBNull.Value;
}
