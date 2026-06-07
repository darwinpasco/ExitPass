using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// PostgreSQL-backed HikCentral vendor exchange audit recorder.
/// </summary>
public sealed class PostgresHikCentralGateActionAuditRecorder : IHikCentralGateActionAuditRecorder
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a durable HikCentral audit recorder using the configured main database.
    /// </summary>
    public PostgresHikCentralGateActionAuditRecorder(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        HikCentralGateActionAuditRecord record,
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
                event_payload_ref,
                event_payload_hash,
                source_event_ref,
                occurred_at,
                received_at,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            SELECT
                @audit_id,
                COALESCE(@gate_device_id, gac.gate_device_id),
                gac.gate_authorization_consumption_id,
                gac.exit_authorization_id,
                COALESCE(@site_id, gac.site_id),
                COALESCE(@lane_id, gac.lane_id),
                CASE
                    WHEN @succeeded THEN 'GATE_OPEN_ACKNOWLEDGED'::gates.gate_event_type_enum
                    ELSE 'GATE_OPEN_FAILED'::gates.gate_event_type_enum
                END,
                CASE
                    WHEN @succeeded THEN 'SUCCESS'::gates.gate_event_status_enum
                    ELSE 'FAILED'::gates.gate_event_status_enum
                END,
                @event_reason_code,
                @event_payload_ref,
                @event_payload_hash,
                @source_event_ref,
                @occurred_at,
                @received_at,
                @correlation_id,
                @created_at,
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

        AddParameters(command, record);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0)
        {
            throw new InvalidOperationException(
                $"Gate authorization consumption '{record.GateAuthorizationConsumptionId}' was not found for HikCentral audit.");
        }
    }

    private static void AddParameters(NpgsqlCommand command, HikCentralGateActionAuditRecord record)
    {
        command.Parameters.Add("audit_id", NpgsqlDbType.Uuid).Value = record.AuditId;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            record.GateAuthorizationConsumptionId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = DbValue(record.GateDeviceId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(record.SiteId);
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = DbValue(record.LaneId);
        command.Parameters.Add("succeeded", NpgsqlDbType.Boolean).Value =
            string.Equals(record.OutcomeCategory, "Succeeded", StringComparison.OrdinalIgnoreCase);
        command.Parameters.Add("event_reason_code", NpgsqlDbType.Varchar).Value =
            DbValue(record.VendorResponseCode ?? record.TransportErrorCode ?? record.OutcomeCategory);
        command.Parameters.Add("event_payload_ref", NpgsqlDbType.Varchar).Value =
            DbValue($"{record.VendorCode}:{record.Operation}:{record.RequestPath}");
        command.Parameters.Add("event_payload_hash", NpgsqlDbType.Char).Value = record.RequestBodySha256;
        command.Parameters.Add("source_event_ref", NpgsqlDbType.Varchar).Value =
            DbValue(record.SourceEventId == Guid.Empty
                ? null
                : $"central-pms://integration-events/{record.SourceEventId}");
        command.Parameters.Add("occurred_at", NpgsqlDbType.TimestampTz).Value = record.RequestedAtUtc;
        command.Parameters.Add("received_at", NpgsqlDbType.TimestampTz).Value = record.RespondedAtUtc;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = record.RequestCorrelationId;
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = record.CreatedAtUtc;
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value.Value : DBNull.Value;
}
