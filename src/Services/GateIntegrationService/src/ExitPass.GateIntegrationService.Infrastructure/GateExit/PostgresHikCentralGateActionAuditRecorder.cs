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
            INSERT INTO gates.hikcentral_gate_action_audits (
                audit_id,
                gate_command_id,
                source_processing_id,
                source_event_id,
                exit_authorization_id,
                gate_authorization_consumption_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                gate_device_identifier,
                door_index_code,
                lane_id,
                site_id,
                vendor_system_id,
                vendor_code,
                vendor_name,
                operation,
                request_method,
                request_path,
                request_body_sha256,
                signed_headers_list,
                request_correlation_id,
                vendor_request_id,
                vendor_correlation_id,
                http_status_code,
                vendor_response_code,
                vendor_response_message,
                outcome_category,
                retryable,
                terminal_failure,
                duration_ms,
                timeout_occurred,
                vendor_unavailable,
                transport_error_code,
                transport_error_message,
                requested_at,
                responded_at,
                created_at
            )
            VALUES (
                @audit_id,
                @gate_command_id,
                @source_processing_id,
                @source_event_id,
                @exit_authorization_id,
                @gate_authorization_consumption_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @gate_device_identifier,
                @door_index_code,
                @lane_id,
                @site_id,
                @vendor_system_id,
                @vendor_code,
                @vendor_name,
                @operation,
                @request_method,
                @request_path,
                @request_body_sha256,
                @signed_headers_list,
                @request_correlation_id,
                @vendor_request_id,
                @vendor_correlation_id,
                @http_status_code,
                @vendor_response_code,
                @vendor_response_message,
                @outcome_category,
                @retryable,
                @terminal_failure,
                @duration_ms,
                @timeout_occurred,
                @vendor_unavailable,
                @transport_error_code,
                @transport_error_message,
                @requested_at,
                @responded_at,
                @created_at
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        AddParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(NpgsqlCommand command, HikCentralGateActionAuditRecord record)
    {
        command.Parameters.Add("audit_id", NpgsqlDbType.Uuid).Value = record.AuditId;
        command.Parameters.Add("gate_command_id", NpgsqlDbType.Uuid).Value = record.GateCommandId;
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = record.SourceProcessingId;
        command.Parameters.Add("source_event_id", NpgsqlDbType.Uuid).Value = DbValue(record.SourceEventId == Guid.Empty ? null : record.SourceEventId);
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = record.ExitAuthorizationId;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = record.GateAuthorizationConsumptionId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = record.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = record.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = record.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = DbValue(record.GateDeviceId);
        command.Parameters.Add("gate_device_identifier", NpgsqlDbType.Varchar).Value = DbValue(record.GateDeviceIdentifier);
        command.Parameters.Add("door_index_code", NpgsqlDbType.Varchar).Value = record.DoorIndexCode;
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = DbValue(record.LaneId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(record.SiteId);
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = DbValue(record.VendorSystemId);
        command.Parameters.Add("vendor_code", NpgsqlDbType.Varchar).Value = record.VendorCode;
        command.Parameters.Add("vendor_name", NpgsqlDbType.Varchar).Value = record.VendorName;
        command.Parameters.Add("operation", NpgsqlDbType.Varchar).Value = record.Operation;
        command.Parameters.Add("request_method", NpgsqlDbType.Varchar).Value = record.RequestMethod;
        command.Parameters.Add("request_path", NpgsqlDbType.Varchar).Value = record.RequestPath;
        command.Parameters.Add("request_body_sha256", NpgsqlDbType.Char).Value = record.RequestBodySha256;
        command.Parameters.Add("signed_headers_list", NpgsqlDbType.Text).Value = record.SignedHeadersList;
        command.Parameters.Add("request_correlation_id", NpgsqlDbType.Uuid).Value = record.RequestCorrelationId;
        command.Parameters.Add("vendor_request_id", NpgsqlDbType.Varchar).Value = DbValue(record.VendorRequestId);
        command.Parameters.Add("vendor_correlation_id", NpgsqlDbType.Varchar).Value = DbValue(record.VendorCorrelationId);
        command.Parameters.Add("http_status_code", NpgsqlDbType.Integer).Value = DbValue(record.HttpStatusCode);
        command.Parameters.Add("vendor_response_code", NpgsqlDbType.Varchar).Value = DbValue(record.VendorResponseCode);
        command.Parameters.Add("vendor_response_message", NpgsqlDbType.Varchar).Value = DbValue(record.VendorResponseMessage);
        command.Parameters.Add("outcome_category", NpgsqlDbType.Varchar).Value = record.OutcomeCategory;
        command.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = record.Retryable;
        command.Parameters.Add("terminal_failure", NpgsqlDbType.Boolean).Value = record.TerminalFailure;
        command.Parameters.Add("duration_ms", NpgsqlDbType.Integer).Value = record.DurationMs;
        command.Parameters.Add("timeout_occurred", NpgsqlDbType.Boolean).Value = record.TimeoutOccurred;
        command.Parameters.Add("vendor_unavailable", NpgsqlDbType.Boolean).Value = record.VendorUnavailable;
        command.Parameters.Add("transport_error_code", NpgsqlDbType.Varchar).Value = DbValue(record.TransportErrorCode);
        command.Parameters.Add("transport_error_message", NpgsqlDbType.Text).Value = DbValue(record.TransportErrorMessage);
        command.Parameters.Add("requested_at", NpgsqlDbType.TimestampTz).Value = record.RequestedAtUtc;
        command.Parameters.Add("responded_at", NpgsqlDbType.TimestampTz).Value = record.RespondedAtUtc;
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = record.CreatedAtUtc;
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value.Value : DBNull.Value;

    private static object DbValue(int? value) =>
        value.HasValue ? value.Value : DBNull.Value;
}
