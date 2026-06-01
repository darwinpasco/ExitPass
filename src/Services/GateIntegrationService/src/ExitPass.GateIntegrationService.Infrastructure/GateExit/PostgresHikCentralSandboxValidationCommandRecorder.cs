using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// PostgreSQL recorder for validation-only HikCentral sandbox command rows.
/// </summary>
public sealed class PostgresHikCentralSandboxValidationCommandRecorder
    : IHikCentralSandboxValidationCommandRecorder
{
    private const string CommandType = "HikCentralSandboxValidation";
    private readonly string _connectionString;

    /// <summary>
    /// Creates a durable validation command recorder using the configured main database.
    /// </summary>
    public PostgresHikCentralSandboxValidationCommandRecorder(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <inheritdoc />
    public async Task<GateCommandLifecycleRecord> BeginValidationCommandAsync(
        HikCentralSandboxValidationCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var commandId = Guid.NewGuid();
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
                NULL,
                @exit_authorization_id,
                @gate_authorization_consumption_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                NULL,
                @gate_device_identifier,
                NULL,
                NULL,
                NULL,
                'IN_PROGRESS',
                1,
                1,
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
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
        command.Parameters.Add("command_type", NpgsqlDbType.Varchar).Value = CommandType;
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = context.ValidationAttemptId;
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = context.ExitAuthorizationId;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = context.GateAuthorizationConsumptionId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = context.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = context.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = context.TariffSnapshotId;
        command.Parameters.Add("gate_device_identifier", NpgsqlDbType.Varchar).Value = context.DoorIndexCode;
        command.Parameters.Add("retry_policy_code", NpgsqlDbType.Varchar).Value = GateCommandRetryPolicy.Default.PolicyCode;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = context.CorrelationId;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = context.RequestedAtUtc;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new GateCommandLifecycleRecord(
            commandId,
            context.ValidationAttemptId,
            Guid.Empty,
            context.ExitAuthorizationId,
            context.GateAuthorizationConsumptionId,
            context.ParkingSessionId,
            context.PaymentAttemptId,
            context.TariffSnapshotId,
            null,
            context.DoorIndexCode,
            null,
            null,
            null,
            GateCommandStatus.InProgress,
            1,
            1,
            GateCommandRetryPolicy.Default.PolicyCode,
            context.RequestedAtUtc,
            context.RequestedAtUtc,
            context.RequestedAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            context.CorrelationId);
    }

    /// <inheritdoc />
    public async Task CompleteValidationCommandAsync(
        Guid commandId,
        bool succeeded,
        string resultCode,
        string diagnosticMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = CASE WHEN @succeeded THEN 'SUCCEEDED' ELSE 'FAILED' END,
                completed_at = @completed_at,
                failure_code = CASE WHEN @succeeded THEN NULL ELSE @result_code END,
                failure_reason = CASE WHEN @succeeded THEN NULL ELSE @diagnostic_message END,
                last_failure_code = CASE WHEN @succeeded THEN NULL ELSE @result_code END,
                last_failure_reason = CASE WHEN @succeeded THEN NULL ELSE @diagnostic_message END,
                updated_at = @completed_at
            WHERE command_id = @command_id
              AND command_type = @command_type;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
        command.Parameters.Add("command_type", NpgsqlDbType.Varchar).Value = CommandType;
        command.Parameters.Add("succeeded", NpgsqlDbType.Boolean).Value = succeeded;
        command.Parameters.Add("result_code", NpgsqlDbType.Varchar).Value = resultCode;
        command.Parameters.Add("diagnostic_message", NpgsqlDbType.Text).Value = diagnosticMessage;
        command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = completedAtUtc;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
