using System.Data;
using ExitPass.GateIntegrationService.Application.GateExit;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// PostgreSQL-backed consumed authorization handoff recorder for durable idempotency.
/// </summary>
public sealed class PostgresGateAuthorizationConsumedProcessingRecorder
    : IGateAuthorizationConsumedProcessingRecorder
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a durable processing recorder using the configured main database.
    /// </summary>
    public PostgresGateAuthorizationConsumedProcessingRecorder(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <inheritdoc />
    public async Task<GateAuthorizationConsumedProcessingStart> BeginProcessingAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        const string sql = """
            SELECT
                gac.gate_authorization_consumption_id,
                gac.exit_authorization_id,
                gac.command_result_status::text AS command_result_status,
                gac.command_result_at,
                gac.failure_detail,
                COALESCE(command_attempts.attempt_count, 0)::integer AS attempt_count
            FROM gates.gate_authorization_consumptions AS gac
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::integer AS attempt_count
                FROM gates.gate_events AS ge
                WHERE ge.gate_authorization_consumption_id = gac.gate_authorization_consumption_id
                  AND ge.event_type = 'GATE_OPEN_COMMAND_REQUESTED'
            ) AS command_attempts ON TRUE
            WHERE gac.gate_authorization_consumption_id = @gate_authorization_consumption_id
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            handoff.GateAuthorizationConsumptionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Gate authorization consumption '{handoff.GateAuthorizationConsumptionId}' was not found.");
        }

        var commandStatus = ReadNullableString(reader, "command_result_status");
        var processedAt = ReadNullableDateTimeOffset(reader, "command_result_at") ?? handoff.ConsumedAtUtc;
        var attemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count"));
        var processingStatus = MapProcessingStatus(commandStatus);
        var failureCode = processingStatus == GateAuthorizationConsumedProcessingStatus.Failed
            ? "GATE_HANDOFF_ADAPTER_FAILED"
            : null;

        var record = new GateAuthorizationConsumedProcessingRecord(
            handoff.EventId,
            ReadNullableGuid(reader, "exit_authorization_id") ?? handoff.ExitAuthorizationId,
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            handoff.TariffSnapshotId,
            ResolveResultCode(processingStatus),
            processedAt,
            processingStatus,
            Math.Max(1, attemptCount),
            failureCode,
            failureCode is null ? null : ReadNullableString(reader, "failure_detail"));
        var alreadyProcessed = processingStatus == GateAuthorizationConsumedProcessingStatus.Processed;
        var alreadyInProgress = commandStatus is "REQUESTED" or "ACKNOWLEDGED";
        var canRetryFailure = processingStatus == GateAuthorizationConsumedProcessingStatus.Failed;

        return new GateAuthorizationConsumedProcessingStart(
            record,
            CanInvokeAdapter: !alreadyProcessed && (!alreadyInProgress || canRetryFailure),
            AlreadyProcessed: alreadyProcessed,
            AlreadyInProgress: alreadyInProgress && !canRetryFailure);
    }

    /// <inheritdoc />
    public async Task RecordProcessedAsync(
        GateAuthorizationConsumedProcessingRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

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
                occurred_at,
                received_at,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            SELECT
                gen_random_uuid(),
                gac.gate_device_id,
                gac.gate_authorization_consumption_id,
                gac.exit_authorization_id,
                gac.site_id,
                gac.lane_id,
                'GATE_OPEN_ACKNOWLEDGED',
                'SUCCESS',
                @result_code,
                @processed_at,
                @processed_at,
                gac.correlation_id,
                @processed_at,
                gac.created_by_service_identity_id
            FROM gates.gate_authorization_consumptions AS gac
            WHERE gac.gate_authorization_consumption_id = @gate_authorization_consumption_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            record.GateAuthorizationConsumptionId;
        command.Parameters.Add("result_code", NpgsqlDbType.Varchar).Value = record.ResultCode;
        command.Parameters.Add("processed_at", NpgsqlDbType.TimestampTz).Value = record.ProcessedAtUtc;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordFailedAsync(
        GateAuthorizationConsumedHandoff handoff,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        var now = DateTimeOffset.UtcNow;
        const string sql = """
            WITH updated AS (
                UPDATE gates.gate_authorization_consumptions
                SET
                    command_result_status = CASE
                        WHEN command_requested THEN 'FAILED'
                        ELSE command_result_status
                    END,
                    command_result_at = CASE
                        WHEN command_requested THEN @now
                        ELSE command_result_at
                    END,
                    failure_detail = @failure_reason,
                    updated_at = @now,
                    row_version = row_version + 1
                WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
                RETURNING *
            )
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
                source_event_ref,
                occurred_at,
                received_at,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            SELECT
                gen_random_uuid(),
                updated.gate_device_id,
                updated.gate_authorization_consumption_id,
                updated.exit_authorization_id,
                updated.site_id,
                updated.lane_id,
                CASE
                    WHEN updated.command_requested THEN 'GATE_OPEN_FAILED'::gates.gate_event_type_enum
                    ELSE 'AUTHORIZATION_DENIED'::gates.gate_event_type_enum
                END,
                'FAILED',
                @failure_code,
                @source_event_ref,
                @now,
                @now,
                updated.correlation_id,
                @now,
                updated.created_by_service_identity_id
            FROM updated;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            handoff.GateAuthorizationConsumptionId;
        command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value = failureCode;
        command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value = failureReason;
        command.Parameters.Add("source_event_ref", NpgsqlDbType.Varchar).Value = DbValue(handoff.SourceEventRef);
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static GateAuthorizationConsumedProcessingStatus MapProcessingStatus(string? commandStatus) =>
        commandStatus switch
        {
            "OPENED" => GateAuthorizationConsumedProcessingStatus.Processed,
            "FAILED" or "TIMEOUT" or "UNKNOWN" => GateAuthorizationConsumedProcessingStatus.Failed,
            _ => GateAuthorizationConsumedProcessingStatus.Processing
        };

    private static string ResolveResultCode(GateAuthorizationConsumedProcessingStatus status) =>
        status switch
        {
            GateAuthorizationConsumedProcessingStatus.Processed => "GATE_AUTHORIZATION_CONSUMED_PROCESSED",
            GateAuthorizationConsumedProcessingStatus.Failed => "GATE_AUTHORIZATION_CONSUMED_FAILED",
            _ => "GATE_AUTHORIZATION_CONSUMED_PROCESSING"
        };

    private static string? ReadNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static Guid? ReadNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
