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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var state = await ReadConsumptionStateAsync(connection, transaction, handoff, cancellationToken);
        if (state is null)
        {
            throw new InvalidOperationException(
                $"Gate authorization consumption '{handoff.GateAuthorizationConsumptionId}' was not found.");
        }

        var attemptCount = await CountCommandAttemptsAsync(
            connection,
            transaction,
            handoff.GateAuthorizationConsumptionId,
            cancellationToken);
        var status = MapCommandStatus(state, attemptCount);

        if (status is GateCommandStatus.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
            return new GateCommandLifecycleStart(
                CreateRecord(state, ResolveExistingCommandId(state), handoff, status, attemptCount, now, null, null),
                Created: false,
                CanInvokeAdapter: false);
        }

        if (status is GateCommandStatus.TerminalFailure)
        {
            await transaction.CommitAsync(cancellationToken);
            return new GateCommandLifecycleStart(
                CreateRecord(state, ResolveExistingCommandId(state), handoff, status, attemptCount, now, null, now),
                Created: false,
                CanInvokeAdapter: false);
        }

        var commandId = Guid.NewGuid();
        var created = attemptCount == 0;
        var nextAttempt = attemptCount + 1;
        await InsertCommandEventAsync(connection, transaction, handoff, commandId, now, cancellationToken);
        await MarkCommandRequestedAsync(connection, transaction, state, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new GateCommandLifecycleStart(
            CreateRecord(state, commandId, handoff, GateCommandStatus.InProgress, nextAttempt, now, now, null),
            Created: created,
            CanInvokeAdapter: true);
    }

    /// <inheritdoc />
    public async Task RecordSucceededAsync(
        Guid commandId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH updated_event AS (
                UPDATE gates.gate_events
                SET
                    event_status = 'SUCCESS',
                    event_reason_code = 'GATE_COMMAND_SUCCEEDED'
                WHERE gate_event_id = @command_id
                RETURNING gate_authorization_consumption_id
            )
            UPDATE gates.gate_authorization_consumptions AS gac
            SET
                command_requested = TRUE,
                command_result_status = 'OPENED',
                command_result_at = @completed_at,
                failure_detail = NULL,
                updated_at = @completed_at,
                row_version = row_version + 1
            FROM updated_event
            WHERE gac.gate_authorization_consumption_id = updated_event.gate_authorization_consumption_id;
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
        var completedAtUtc = DateTimeOffset.UtcNow;
        const string sql = """
            WITH updated_event AS (
                UPDATE gates.gate_events
                SET
                    event_status = CASE
                        WHEN @retryable THEN 'FAILED'::gates.gate_event_status_enum
                        ELSE 'ERROR'::gates.gate_event_status_enum
                    END,
                    event_reason_code = @failure_code
                WHERE gate_event_id = @command_id
                RETURNING gate_authorization_consumption_id
            )
            UPDATE gates.gate_authorization_consumptions AS gac
            SET
                command_requested = TRUE,
                command_result_status = 'FAILED',
                command_result_at = @completed_at,
                failure_detail = @failure_reason,
                updated_at = @completed_at,
                row_version = row_version + 1
            FROM updated_event
            WHERE gac.gate_authorization_consumption_id = updated_event.gate_authorization_consumption_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
        command.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = retryable;
        command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = completedAtUtc;
        command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value = failureCode;
        command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value = failureReason;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ConsumptionState?> ReadConsumptionStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                gate_authorization_consumption_id,
                exit_authorization_id,
                gate_device_id,
                site_id,
                lane_id,
                command_requested,
                command_result_status::text AS command_result_status,
                command_result_at,
                failure_detail,
                correlation_id,
                created_by_service_identity_id
            FROM gates.gate_authorization_consumptions
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            handoff.GateAuthorizationConsumptionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ConsumptionState(
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            ReadNullableGuid(reader, "exit_authorization_id") ?? handoff.ExitAuthorizationId,
            ReadNullableGuid(reader, "gate_device_id") ?? handoff.GateDeviceId,
            reader.GetGuid(reader.GetOrdinal("site_id")),
            ReadNullableGuid(reader, "lane_id") ?? handoff.LaneId,
            reader.GetBoolean(reader.GetOrdinal("command_requested")),
            ReadNullableString(reader, "command_result_status"),
            ReadNullableDateTimeOffset(reader, "command_result_at"),
            ReadNullableString(reader, "failure_detail"),
            ReadNullableGuid(reader, "correlation_id") ?? handoff.CorrelationId,
            reader.GetGuid(reader.GetOrdinal("created_by_service_identity_id")));
    }

    private static async Task<int> CountCommandAttemptsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)::integer
            FROM gates.gate_events
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
              AND event_type = 'GATE_OPEN_COMMAND_REQUESTED';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            gateAuthorizationConsumptionId;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int value ? value : Convert.ToInt32(result);
    }

    private static async Task InsertCommandEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GateAuthorizationConsumedHandoff handoff,
        Guid commandId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO gates.gate_events (
                gate_event_id,
                gate_device_id,
                gate_authorization_consumption_id,
                exit_authorization_id,
                site_id,
                lane_id,
                event_type,
                event_status,
                event_reason_code,
                event_payload_ref,
                source_event_ref,
                occurred_at,
                received_at,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            SELECT
                @gate_event_id,
                COALESCE(@gate_device_id, gac.gate_device_id),
                gac.gate_authorization_consumption_id,
                gac.exit_authorization_id,
                COALESCE(@site_id, gac.site_id),
                COALESCE(@lane_id, gac.lane_id),
                'GATE_OPEN_COMMAND_REQUESTED',
                'RECORDED',
                'GATE_COMMAND_REQUESTED',
                @event_payload_ref,
                @source_event_ref,
                @occurred_at,
                @received_at,
                @correlation_id,
                @created_at,
                gac.created_by_service_identity_id
            FROM gates.gate_authorization_consumptions AS gac
            WHERE gac.gate_authorization_consumption_id = @gate_authorization_consumption_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_event_id", NpgsqlDbType.Uuid).Value = commandId;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            handoff.GateAuthorizationConsumptionId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.GateDeviceId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.SiteId);
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.LaneId);
        command.Parameters.Add("event_payload_ref", NpgsqlDbType.Varchar).Value =
            DbValue($"gate-command-retry={GateCommandRetryPolicy.Default.PolicyCode}");
        command.Parameters.Add("source_event_ref", NpgsqlDbType.Varchar).Value = DbValue(handoff.SourceEventRef);
        command.Parameters.Add("occurred_at", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("received_at", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = handoff.CorrelationId;
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = now;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkCommandRequestedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ConsumptionState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_authorization_consumptions
            SET
                command_requested = TRUE,
                command_result_status = 'REQUESTED',
                command_result_at = @command_result_at,
                failure_detail = NULL,
                updated_at = @updated_at,
                row_version = row_version + 1
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            state.GateAuthorizationConsumptionId;
        command.Parameters.Add("command_result_at", NpgsqlDbType.TimestampTz).Value = now;
        command.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = now;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private GateCommandStatus MapCommandStatus(ConsumptionState state, int attemptCount)
    {
        if (string.Equals(state.CommandResultStatus, "OPENED", StringComparison.OrdinalIgnoreCase))
        {
            return GateCommandStatus.Succeeded;
        }

        if (string.Equals(state.CommandResultStatus, "FAILED", StringComparison.OrdinalIgnoreCase)
            && !_retryPolicy.HasAttemptsRemaining(attemptCount))
        {
            return GateCommandStatus.TerminalFailure;
        }

        if (string.Equals(state.CommandResultStatus, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return GateCommandStatus.Retryable;
        }

        return GateCommandStatus.Requested;
    }

    private static GateCommandLifecycleRecord CreateRecord(
        ConsumptionState state,
        Guid commandId,
        GateAuthorizationConsumedHandoff handoff,
        GateCommandStatus status,
        int attemptCount,
        DateTimeOffset now,
        DateTimeOffset? startedAt,
        DateTimeOffset? terminalFailureAt)
    {
        DateTimeOffset? completedAt = status is GateCommandStatus.Succeeded or GateCommandStatus.Failed or GateCommandStatus.TerminalFailure
            ? state.CommandResultAt ?? now
            : null;
        var failureCode = status is GateCommandStatus.Retryable or GateCommandStatus.Failed or GateCommandStatus.TerminalFailure
            ? "GATE_HANDOFF_ADAPTER_FAILED"
            : null;

        return new GateCommandLifecycleRecord(
            commandId,
            ResolveProcessingKey(handoff),
            handoff.EventId,
            state.ExitAuthorizationId,
            state.GateAuthorizationConsumptionId,
            handoff.ParkingSessionId,
            handoff.PaymentAttemptId,
            handoff.TariffSnapshotId,
            state.GateDeviceId,
            handoff.GateDeviceIdentifier,
            state.LaneId,
            state.SiteId,
            handoff.VendorSystemId,
            status,
            Math.Max(1, attemptCount),
            GateCommandRetryPolicy.Default.MaxAttempts,
            GateCommandRetryPolicy.Default.PolicyCode,
            handoff.ConsumedAtUtc,
            now,
            startedAt,
            completedAt,
            status is GateCommandStatus.Retryable ? now.Add(GateCommandRetryPolicy.Default.RetryDelay) : null,
            terminalFailureAt,
            failureCode,
            failureCode is null ? null : state.FailureDetail,
            failureCode,
            failureCode is null ? null : state.FailureDetail,
            state.CorrelationId);
    }

    private static Guid ResolveExistingCommandId(ConsumptionState state) =>
        state.GateAuthorizationConsumptionId;

    private static Guid ResolveProcessingKey(GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;

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

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value.Value : DBNull.Value;

    private sealed record ConsumptionState(
        Guid GateAuthorizationConsumptionId,
        Guid ExitAuthorizationId,
        Guid? GateDeviceId,
        Guid SiteId,
        Guid? LaneId,
        bool CommandRequested,
        string? CommandResultStatus,
        DateTimeOffset? CommandResultAt,
        string? FailureDetail,
        Guid CorrelationId,
        Guid CreatedByServiceIdentityId);
}
