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
    private const string EventType = "GateAuthorizationConsumed";
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

        var now = DateTimeOffset.UtcNow;
        var processingKey = ResolveProcessingKey(handoff);

        const string sql = """
            INSERT INTO gates.gate_authorization_consumed_processing (
                processing_id,
                processing_key,
                event_id,
                event_type,
                source_event_ref,
                gate_authorization_consumption_id,
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                gate_device_identifier,
                lane_id,
                site_id,
                vendor_system_id,
                consumed_at_utc,
                correlation_id,
                processing_status,
                result_code,
                attempt_count,
                first_seen_at,
                last_attempted_at,
                created_at,
                updated_at
            )
            VALUES (
                @processing_id,
                @processing_key,
                @event_id,
                @event_type,
                @source_event_ref,
                @gate_authorization_consumption_id,
                @exit_authorization_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @gate_device_identifier,
                @lane_id,
                @site_id,
                @vendor_system_id,
                @consumed_at_utc,
                @correlation_id,
                'PROCESSING',
                'GATE_AUTHORIZATION_CONSUMED_PROCESSING',
                1,
                @now,
                @now,
                @now,
                @now
            )
            ON CONFLICT (processing_key, event_type) DO UPDATE
            SET
                processing_status = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN 'PROCESSING'
                    ELSE gates.gate_authorization_consumed_processing.processing_status
                END,
                result_code = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN 'GATE_AUTHORIZATION_CONSUMED_PROCESSING'
                    ELSE gates.gate_authorization_consumed_processing.result_code
                END,
                attempt_count = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN gates.gate_authorization_consumed_processing.attempt_count + 1
                    ELSE gates.gate_authorization_consumed_processing.attempt_count
                END,
                last_attempted_at = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN @now
                    ELSE gates.gate_authorization_consumed_processing.last_attempted_at
                END,
                last_failure_code = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN NULL
                    ELSE gates.gate_authorization_consumed_processing.last_failure_code
                END,
                last_failure_reason = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN NULL
                    ELSE gates.gate_authorization_consumed_processing.last_failure_reason
                END,
                updated_at = CASE
                    WHEN gates.gate_authorization_consumed_processing.processing_status = 'FAILED'
                        THEN @now
                    ELSE gates.gate_authorization_consumed_processing.updated_at
                END
            RETURNING
                event_id,
                exit_authorization_id,
                gate_authorization_consumption_id,
                tariff_snapshot_id,
                result_code,
                COALESCE(processed_at, last_attempted_at, first_seen_at) AS processed_at_utc,
                processing_status,
                attempt_count,
                last_failure_code,
                last_failure_reason,
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
            throw new InvalidOperationException("GateAuthorizationConsumed processing state was not returned.");
        }

        var record = ReadRecord(reader);
        var inserted = reader.GetBoolean(reader.GetOrdinal("inserted"));
        var canInvokeAdapter = inserted || record.ProcessingStatus == GateAuthorizationConsumedProcessingStatus.Processing
            && record.AttemptCount > 1
            && record.LastFailureCode is null;
        var alreadyProcessed = record.ProcessingStatus == GateAuthorizationConsumedProcessingStatus.Processed;

        return new GateAuthorizationConsumedProcessingStart(
            record,
            CanInvokeAdapter: canInvokeAdapter && !alreadyProcessed,
            AlreadyProcessed: alreadyProcessed,
            AlreadyInProgress: !inserted && !alreadyProcessed && !canInvokeAdapter);
    }

    /// <inheritdoc />
    public async Task RecordProcessedAsync(
        GateAuthorizationConsumedProcessingRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        const string sql = """
            UPDATE gates.gate_authorization_consumed_processing
            SET
                processing_status = 'PROCESSED',
                result_code = @result_code,
                processed_at = @processed_at,
                last_failure_code = NULL,
                last_failure_reason = NULL,
                updated_at = @processed_at
            WHERE processing_key = @processing_key
              AND event_type = @event_type;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = record.ProcessingKey;
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = EventType;
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

        const string sql = """
            UPDATE gates.gate_authorization_consumed_processing
            SET
                processing_status = 'FAILED',
                result_code = @failure_code,
                processed_at = NULL,
                last_failure_code = @failure_code,
                last_failure_reason = @failure_reason,
                updated_at = @now
            WHERE processing_key = @processing_key
              AND event_type = @event_type;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = ResolveProcessingKey(handoff);
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = EventType;
        command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value = failureCode;
        command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value = failureReason;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.UtcNow;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddHandoffParameters(
        NpgsqlCommand command,
        GateAuthorizationConsumedHandoff handoff,
        Guid processingKey,
        DateTimeOffset now)
    {
        command.Parameters.Add("processing_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = processingKey;
        command.Parameters.Add("event_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.EventId == Guid.Empty ? null : handoff.EventId);
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = EventType;
        command.Parameters.Add("source_event_ref", NpgsqlDbType.Varchar).Value = DbValue(handoff.SourceEventRef);
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = handoff.GateAuthorizationConsumptionId;
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = handoff.ExitAuthorizationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = handoff.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = handoff.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = handoff.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.GateDeviceId);
        command.Parameters.Add("gate_device_identifier", NpgsqlDbType.Varchar).Value = DbValue(handoff.GateDeviceIdentifier);
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.LaneId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.SiteId);
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = DbValue(handoff.VendorSystemId);
        command.Parameters.Add("consumed_at_utc", NpgsqlDbType.TimestampTz).Value = handoff.ConsumedAtUtc;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = handoff.CorrelationId;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;
    }

    private static GateAuthorizationConsumedProcessingRecord ReadRecord(NpgsqlDataReader reader)
    {
        var eventId = reader.IsDBNull(reader.GetOrdinal("event_id"))
            ? Guid.Empty
            : reader.GetGuid(reader.GetOrdinal("event_id"));
        var status = ParseStatus(reader.GetString(reader.GetOrdinal("processing_status")));
        var processedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("processed_at_utc"));

        return new GateAuthorizationConsumedProcessingRecord(
            eventId,
            reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("result_code")),
            processedAt,
            status,
            reader.GetInt32(reader.GetOrdinal("attempt_count")),
            ReadNullableString(reader, "last_failure_code"),
            ReadNullableString(reader, "last_failure_reason"));
    }

    private static GateAuthorizationConsumedProcessingStatus ParseStatus(string status) =>
        status switch
        {
            "PROCESSING" => GateAuthorizationConsumedProcessingStatus.Processing,
            "PROCESSED" => GateAuthorizationConsumedProcessingStatus.Processed,
            "FAILED" => GateAuthorizationConsumedProcessingStatus.Failed,
            _ => throw new InvalidOperationException($"Unknown GateAuthorizationConsumed processing status '{status}'.")
        };

    private static string? ReadNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid ResolveProcessingKey(GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value.Value : DBNull.Value;
}
