using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.Gates;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// PostgreSQL-backed single-command gate executor repository.
/// </summary>
public sealed class GateCommandExecutionRepository : IGateCommandExecutionRepository
{
    private const string OpenGateCommandType = "OPEN_GATE";
    private const string RequestedStatus = "REQUESTED";
    private const string InProgressStatus = "IN_PROGRESS";
    private const string SucceededStatus = "SUCCEEDED";
    private const string RetryableStatus = "RETRYABLE";
    private const string TerminalFailureStatus = "TERMINAL_FAILURE";
    private const string FakeRequestPath = "/__fake__/hikcentral/gate-action/open-gate";
    private const string SignedHeaderNames = "";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a gate command execution repository.
    /// </summary>
    public GateCommandExecutionRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateCommandClaimResult> ClaimAsync(
        Guid gateCommandId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        if (gateCommandId == Guid.Empty)
        {
            return Rejected(null, "GATE_COMMAND_ID_REQUIRED", "Gate command id is required.", null);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = await ReadCommandForClaimAsync(connection, transaction, gateCommandId, cancellationToken);
            if (command is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Rejected(null, "GATE_COMMAND_NOT_FOUND", "Gate command does not exist.", null);
            }

            var rejection = ValidateClaimEligibility(command);
            if (rejection is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return rejection;
            }

            var claimed = await MarkClaimedAsync(connection, transaction, command, claimedAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new GateCommandClaimResult(
                GateCommandClaimOutcome.Claimed,
                claimed,
                InProgressStatus,
                ErrorCode: null,
                Message: null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GateCommandClaimResult> ClaimRetryAsync(
        Guid gateCommandId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        if (gateCommandId == Guid.Empty)
        {
            return Rejected(null, "GATE_COMMAND_ID_REQUIRED", "Gate command id is required.", null);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = await ReadCommandForClaimAsync(connection, transaction, gateCommandId, cancellationToken);
            if (command is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Rejected(null, "GATE_COMMAND_NOT_FOUND", "Gate command does not exist.", null);
            }

            var rejection = ValidateRetryClaimEligibility(command, claimedAt);
            if (rejection is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return rejection;
            }

            var claimed = await MarkRetryClaimedAsync(connection, transaction, command, claimedAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new GateCommandClaimResult(
                GateCommandClaimOutcome.Claimed,
                claimed,
                InProgressStatus,
                ErrorCode: null,
                Message: null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GateCommandFinalizationResult> FinalizeAsync(
        GateCommandExecutionClaim claim,
        HikCentralGateActionResult actionResult,
        DateTimeOffset finalizedAt,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(actionResult);

        if (retryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentException("Retry delay must be positive.", nameof(retryDelay));
        }

        ValidateActionResult(actionResult);
        var mapped = MapFinalCommandState(claim, actionResult, retryDelay);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await ReadCommandFinalizationStateAsync(
                connection,
                transaction,
                claim.CommandId,
                cancellationToken);

            if (current is null ||
                !string.Equals(current.CommandStatus, InProgressStatus, StringComparison.Ordinal) ||
                current.AttemptCount != claim.AttemptCount)
            {
                throw new InvalidOperationException("Gate command finalization state no longer matches the claimed execution.");
            }

            var auditId = await InsertAuditAsync(
                connection,
                transaction,
                claim,
                actionResult,
                finalizedAt,
                cancellationToken);

            await UpdateCommandFinalStateAsync(
                connection,
                transaction,
                claim.CommandId,
                mapped,
                finalizedAt,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new GateCommandFinalizationResult(
                claim.CommandId,
                auditId,
                mapped.CommandStatus,
                mapped.NextAttemptAt,
                mapped.CompletedAt,
                mapped.TerminalFailureAt);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<CommandForClaim?> ReadCommandForClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid gateCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                gc.command_id,
                gc.command_type,
                gc.source_processing_id,
                gc.source_event_id,
                gc.source_event_ref,
                gc.gate_authorization_consumption_id,
                gc.exit_authorization_id,
                gc.parking_session_id,
                gc.payment_attempt_id,
                gc.tariff_snapshot_id,
                gc.gate_device_id,
                gc.service_identity_id,
                gc.lane_id,
                gc.site_id,
                gc.vendor_system_id,
                gc.command_status,
                gc.attempt_count,
                gc.max_attempts,
                gc.retry_policy_code,
                gc.requested_at,
                gc.next_attempt_at,
                gc.correlation_id,
                gd.vendor_device_ref,
                gac.gate_authorization_consumption_id AS existing_consumption_id
            FROM gates.gate_commands AS gc
            LEFT JOIN gates.gate_devices AS gd
              ON gd.gate_device_id = gc.gate_device_id
            LEFT JOIN gates.gate_authorization_consumptions AS gac
              ON gac.gate_authorization_consumption_id = gc.gate_authorization_consumption_id
            WHERE gc.command_id = @command_id
            FOR UPDATE OF gc;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = gateCommandId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CommandForClaim(
            reader.GetGuid("command_id"),
            reader.GetString("command_type"),
            reader.GetGuid("source_processing_id"),
            GetNullableGuid(reader, "source_event_id"),
            GetNullableString(reader, "source_event_ref"),
            reader.GetGuid("gate_authorization_consumption_id"),
            reader.GetGuid("exit_authorization_id"),
            reader.GetGuid("parking_session_id"),
            reader.GetGuid("payment_attempt_id"),
            reader.GetGuid("tariff_snapshot_id"),
            GetNullableGuid(reader, "gate_device_id"),
            GetNullableGuid(reader, "service_identity_id"),
            GetNullableGuid(reader, "lane_id"),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "vendor_system_id"),
            reader.GetString("command_status"),
            reader.GetInt32("attempt_count"),
            reader.GetInt32("max_attempts"),
            reader.GetString("retry_policy_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableDateTimeOffset(reader, "next_attempt_at"),
            reader.GetGuid("correlation_id"),
            GetNullableString(reader, "vendor_device_ref"),
            GetNullableGuid(reader, "existing_consumption_id"));
    }

    private static GateCommandClaimResult? ValidateClaimEligibility(CommandForClaim command)
    {
        if (!string.Equals(command.CommandType, OpenGateCommandType, StringComparison.Ordinal))
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_TYPE_UNSUPPORTED", "Only OPEN_GATE commands are supported.", command.CommandStatus);
        }

        if (string.Equals(command.CommandStatus, SucceededStatus, StringComparison.Ordinal))
        {
            return new GateCommandClaimResult(
                GateCommandClaimOutcome.AlreadyCompleted,
                Claim: null,
                command.CommandStatus,
                "GATE_COMMAND_ALREADY_SUCCEEDED",
                "Gate command already succeeded and will not be executed again.");
        }

        if (!string.Equals(command.CommandStatus, RequestedStatus, StringComparison.Ordinal))
        {
            return Rejected(
                command.CommandStatus,
                "GATE_COMMAND_STATUS_NOT_REQUESTED",
                "Gate command must be REQUESTED for this explicit execution slice.",
                command.CommandStatus);
        }

        var requiredContextRejection = ValidateRequiredExecutionContext(command);
        if (requiredContextRejection is not null)
        {
            return requiredContextRejection;
        }

        return null;
    }

    private static GateCommandClaimResult? ValidateRetryClaimEligibility(
        CommandForClaim command,
        DateTimeOffset claimedAt)
    {
        if (!string.Equals(command.CommandType, OpenGateCommandType, StringComparison.Ordinal))
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_TYPE_UNSUPPORTED", "Only OPEN_GATE commands are supported.", command.CommandStatus);
        }

        if (string.Equals(command.CommandStatus, SucceededStatus, StringComparison.Ordinal) ||
            string.Equals(command.CommandStatus, TerminalFailureStatus, StringComparison.Ordinal))
        {
            return new GateCommandClaimResult(
                GateCommandClaimOutcome.AlreadyCompleted,
                Claim: null,
                command.CommandStatus,
                "GATE_COMMAND_ALREADY_TERMINAL",
                "Gate command already reached a terminal lifecycle state and will not be retried.");
        }

        if (!string.Equals(command.CommandStatus, RetryableStatus, StringComparison.Ordinal))
        {
            return Rejected(
                command.CommandStatus,
                "GATE_COMMAND_STATUS_NOT_RETRYABLE",
                "Gate command must be RETRYABLE for this explicit retry execution slice.",
                command.CommandStatus);
        }

        if (command.NextAttemptAt is null)
        {
            return Rejected(
                command.CommandStatus,
                "GATE_COMMAND_NEXT_ATTEMPT_REQUIRED",
                "Gate command next_attempt_at is required for retry execution.",
                command.CommandStatus);
        }

        if (command.NextAttemptAt > claimedAt)
        {
            return Rejected(
                command.CommandStatus,
                "GATE_COMMAND_RETRY_NOT_DUE",
                "Gate command retry is not due yet.",
                command.CommandStatus);
        }

        var requiredContextRejection = ValidateRequiredExecutionContext(command);
        if (requiredContextRejection is not null)
        {
            return requiredContextRejection;
        }

        return null;
    }

    private static GateCommandClaimResult? ValidateRequiredExecutionContext(CommandForClaim command)
    {
        if (command.AttemptCount >= command.MaxAttempts)
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_ATTEMPTS_EXHAUSTED", "Gate command has no remaining attempts.", command.CommandStatus);
        }

        if (command.GateDeviceId is null || command.GateDeviceId.Value == Guid.Empty)
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_GATE_DEVICE_REQUIRED", "Gate command gate device is required.", command.CommandStatus);
        }

        if (command.VendorSystemId is null || command.VendorSystemId.Value == Guid.Empty)
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_VENDOR_SYSTEM_REQUIRED", "Gate command vendor system is required.", command.CommandStatus);
        }

        if (command.SiteId is null || command.SiteId.Value == Guid.Empty)
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_SITE_REQUIRED", "Gate command site is required.", command.CommandStatus);
        }

        if (command.CorrelationId == Guid.Empty)
        {
            return Rejected(command.CommandStatus, "GATE_COMMAND_CORRELATION_REQUIRED", "Gate command correlation id is required.", command.CommandStatus);
        }

        if (command.ExistingConsumptionId is null)
        {
            return Rejected(
                command.CommandStatus,
                "GATE_AUTHORIZATION_CONSUMPTION_NOT_FOUND",
                "Referenced gate authorization consumption does not exist.",
                command.CommandStatus);
        }

        if (string.IsNullOrWhiteSpace(command.TargetResourceCode))
        {
            return Rejected(
                command.CommandStatus,
                "GATE_COMMAND_TARGET_RESOURCE_MISSING",
                "Gate device vendor_device_ref is required as the HikCentral target resource code.",
                command.CommandStatus);
        }

        return null;
    }

    private static async Task<GateCommandExecutionClaim> MarkClaimedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CommandForClaim commandForClaim,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = 'IN_PROGRESS',
                attempt_count = attempt_count + 1,
                started_at = COALESCE(started_at, @claimed_at),
                last_attempted_at = @claimed_at,
                next_attempt_at = NULL,
                updated_at = @claimed_at
            WHERE command_id = @command_id
              AND command_status = 'REQUESTED'
              AND attempt_count < max_attempts
            RETURNING attempt_count, started_at, last_attempted_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandForClaim.CommandId;
        command.Parameters.Add("claimed_at", NpgsqlDbType.TimestampTz).Value = claimedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Gate command claim update did not affect the expected REQUESTED row.");
        }

        var gateDeviceId = commandForClaim.GateDeviceId!.Value;
        var vendorSystemId = commandForClaim.VendorSystemId!.Value;
        return new GateCommandExecutionClaim(
            commandForClaim.CommandId,
            commandForClaim.CommandType,
            commandForClaim.SourceProcessingId,
            commandForClaim.SourceEventId,
            commandForClaim.SourceEventRef,
            commandForClaim.GateAuthorizationConsumptionId,
            commandForClaim.ExitAuthorizationId,
            commandForClaim.ParkingSessionId,
            commandForClaim.PaymentAttemptId,
            commandForClaim.TariffSnapshotId,
            gateDeviceId,
            commandForClaim.ServiceIdentityId,
            commandForClaim.LaneId,
            commandForClaim.SiteId,
            vendorSystemId,
            commandForClaim.CorrelationId,
            reader.GetInt32("attempt_count"),
            commandForClaim.MaxAttempts,
            commandForClaim.RetryPolicyCode,
            commandForClaim.RequestedAt,
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            commandForClaim.TargetResourceCode!.Trim());
    }

    private static async Task<GateCommandExecutionClaim> MarkRetryClaimedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CommandForClaim commandForClaim,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = 'IN_PROGRESS',
                attempt_count = attempt_count + 1,
                started_at = COALESCE(started_at, @claimed_at),
                last_attempted_at = @claimed_at,
                next_attempt_at = NULL,
                completed_at = NULL,
                terminal_failure_at = NULL,
                updated_at = @claimed_at
            WHERE command_id = @command_id
              AND command_status = 'RETRYABLE'
              AND next_attempt_at IS NOT NULL
              AND next_attempt_at <= @claimed_at
              AND attempt_count < max_attempts
            RETURNING attempt_count, started_at, last_attempted_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandForClaim.CommandId;
        command.Parameters.Add("claimed_at", NpgsqlDbType.TimestampTz).Value = claimedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Gate command retry claim update did not affect the expected RETRYABLE row.");
        }

        var gateDeviceId = commandForClaim.GateDeviceId!.Value;
        var vendorSystemId = commandForClaim.VendorSystemId!.Value;
        return new GateCommandExecutionClaim(
            commandForClaim.CommandId,
            commandForClaim.CommandType,
            commandForClaim.SourceProcessingId,
            commandForClaim.SourceEventId,
            commandForClaim.SourceEventRef,
            commandForClaim.GateAuthorizationConsumptionId,
            commandForClaim.ExitAuthorizationId,
            commandForClaim.ParkingSessionId,
            commandForClaim.PaymentAttemptId,
            commandForClaim.TariffSnapshotId,
            gateDeviceId,
            commandForClaim.ServiceIdentityId,
            commandForClaim.LaneId,
            commandForClaim.SiteId,
            vendorSystemId,
            commandForClaim.CorrelationId,
            reader.GetInt32("attempt_count"),
            commandForClaim.MaxAttempts,
            commandForClaim.RetryPolicyCode,
            commandForClaim.RequestedAt,
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            commandForClaim.TargetResourceCode!.Trim());
    }

    private static async Task<CommandFinalizationState?> ReadCommandFinalizationStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid gateCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT command_status, attempt_count
            FROM gates.gate_commands
            WHERE command_id = @command_id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = gateCommandId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CommandFinalizationState(
            reader.GetString("command_status"),
            reader.GetInt32("attempt_count"));
    }

    private static async Task<Guid> InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GateCommandExecutionClaim claim,
        HikCentralGateActionResult actionResult,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO gates.hikcentral_gate_action_audits (
                gate_command_id,
                source_processing_id,
                gate_authorization_consumption_id,
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                gate_device_id,
                service_identity_id,
                lane_id,
                site_id,
                vendor_system_id,
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
                responded_at,
                created_at
            )
            VALUES (
                @gate_command_id,
                @source_processing_id,
                @gate_authorization_consumption_id,
                @exit_authorization_id,
                @parking_session_id,
                @payment_attempt_id,
                @tariff_snapshot_id,
                @gate_device_id,
                @service_identity_id,
                @lane_id,
                @site_id,
                @vendor_system_id,
                @vendor_code,
                @vendor_operation,
                @door_index_code,
                @request_method,
                @request_path,
                @request_hash,
                @signed_header_names,
                @request_correlation_id,
                @vendor_correlation_id,
                @http_status_code,
                @vendor_result_code,
                @vendor_result_message,
                @action_outcome,
                @retryable,
                @failure_recorded,
                @duration_ms,
                @timed_out,
                @vendor_unavailable,
                @transport_failure,
                @requested_at,
                @responded_at,
                @created_at
            )
            RETURNING hikcentral_gate_action_audit_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("gate_command_id", NpgsqlDbType.Uuid).Value = claim.CommandId;
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = claim.SourceProcessingId;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = claim.GateAuthorizationConsumptionId;
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = claim.ExitAuthorizationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = claim.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = claim.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = claim.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = claim.GateDeviceId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)claim.ServiceIdentityId ?? DBNull.Value;
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = (object?)claim.LaneId ?? DBNull.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = (object?)claim.SiteId ?? DBNull.Value;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = claim.VendorSystemId;
        command.Parameters.Add("vendor_code", NpgsqlDbType.Varchar).Value = actionResult.VendorCode;
        command.Parameters.Add("vendor_operation", NpgsqlDbType.Varchar).Value = actionResult.VendorOperation;
        command.Parameters.Add("door_index_code", NpgsqlDbType.Varchar).Value = actionResult.TargetResourceCode;
        command.Parameters.Add("request_method", NpgsqlDbType.Varchar).Value = actionResult.RequestMethod;
        command.Parameters.Add("request_path", NpgsqlDbType.Varchar).Value = FakeRequestPath;
        command.Parameters.Add("request_hash", NpgsqlDbType.Char).Value = ComputeRequestHash(claim, actionResult);
        command.Parameters.Add("signed_header_names", NpgsqlDbType.Text).Value = SignedHeaderNames;
        command.Parameters.Add("request_correlation_id", NpgsqlDbType.Uuid).Value = actionResult.RequestCorrelationId;
        command.Parameters.Add("vendor_correlation_id", NpgsqlDbType.Varchar).Value =
            (object?)actionResult.VendorCorrelationId ?? DBNull.Value;
        command.Parameters.Add("http_status_code", NpgsqlDbType.Integer).Value =
            (object?)actionResult.HttpStatusCode ?? DBNull.Value;
        command.Parameters.Add("vendor_result_code", NpgsqlDbType.Varchar).Value =
            (object?)actionResult.VendorResultCode ?? DBNull.Value;
        command.Parameters.Add("vendor_result_message", NpgsqlDbType.Varchar).Value =
            (object?)actionResult.VendorResultMessage ?? DBNull.Value;
        command.Parameters.Add("action_outcome", NpgsqlDbType.Varchar).Value = actionResult.ActionOutcome;
        command.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = actionResult.Retryable;
        command.Parameters.Add("failure_recorded", NpgsqlDbType.Boolean).Value = actionResult.FailureRecorded;
        command.Parameters.Add("duration_ms", NpgsqlDbType.Integer).Value = actionResult.DurationMs;
        command.Parameters.Add("timed_out", NpgsqlDbType.Boolean).Value = actionResult.TimedOut;
        command.Parameters.Add("vendor_unavailable", NpgsqlDbType.Boolean).Value = actionResult.VendorUnavailable;
        command.Parameters.Add("transport_failure", NpgsqlDbType.Boolean).Value = actionResult.TransportFailure;
        command.Parameters.Add("requested_at", NpgsqlDbType.TimestampTz).Value = actionResult.RequestedAt;
        command.Parameters.Add("responded_at", NpgsqlDbType.TimestampTz).Value = actionResult.RespondedAt;
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = createdAt;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }

    private static async Task UpdateCommandFinalStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid commandId,
        FinalCommandState mapped,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_commands
            SET
                command_status = @command_status,
                next_attempt_at = @next_attempt_at,
                completed_at = @completed_at,
                terminal_failure_at = @terminal_failure_at,
                failure_code = @failure_code,
                failure_reason = @failure_reason,
                last_failure_code = @last_failure_code,
                last_failure_reason = @last_failure_reason,
                updated_at = @updated_at
            WHERE command_id = @command_id
              AND command_status = 'IN_PROGRESS';
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;
        command.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = mapped.CommandStatus;
        command.Parameters.Add("next_attempt_at", NpgsqlDbType.TimestampTz).Value =
            (object?)mapped.NextAttemptAt ?? DBNull.Value;
        command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value =
            (object?)mapped.CompletedAt ?? DBNull.Value;
        command.Parameters.Add("terminal_failure_at", NpgsqlDbType.TimestampTz).Value =
            (object?)mapped.TerminalFailureAt ?? DBNull.Value;
        command.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value =
            (object?)mapped.FailureCode ?? DBNull.Value;
        command.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value =
            (object?)mapped.FailureReason ?? DBNull.Value;
        command.Parameters.Add("last_failure_code", NpgsqlDbType.Varchar).Value =
            (object?)mapped.LastFailureCode ?? DBNull.Value;
        command.Parameters.Add("last_failure_reason", NpgsqlDbType.Text).Value =
            (object?)mapped.LastFailureReason ?? DBNull.Value;
        command.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = updatedAt;

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("Gate command finalization update did not affect the claimed row.");
        }
    }

    private static void ValidateActionResult(HikCentralGateActionResult actionResult)
    {
        if (!string.Equals(actionResult.VendorCode, HikCentralGateActionConstants.VendorCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HikCentral action result vendor code is not canonical.");
        }

        if (!string.Equals(actionResult.RequestMethod, HikCentralGateActionConstants.RequestMethod, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HikCentral action result request method is not canonical.");
        }

        if (!string.Equals(actionResult.VendorOperation, HikCentralGateActionConstants.OpenGateOperation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HikCentral action result vendor operation is not supported.");
        }

        if (actionResult.RespondedAt < actionResult.RequestedAt)
        {
            throw new InvalidOperationException("HikCentral action result response timestamp precedes request timestamp.");
        }
    }

    private static FinalCommandState MapFinalCommandState(
        GateCommandExecutionClaim claim,
        HikCentralGateActionResult result,
        TimeSpan retryDelay)
    {
        var completedAt = result.RespondedAt;

        if (string.Equals(result.ActionOutcome, HikCentralGateActionConstants.OutcomeSucceeded, StringComparison.Ordinal))
        {
            return new FinalCommandState(
                SucceededStatus,
                NextAttemptAt: null,
                CompletedAt: completedAt,
                TerminalFailureAt: null,
                FailureCode: null,
                FailureReason: null,
                LastFailureCode: null,
                LastFailureReason: null);
        }

        var failureCode = string.IsNullOrWhiteSpace(result.VendorResultCode)
            ? result.ActionOutcome
            : result.VendorResultCode;
        var failureReason = string.IsNullOrWhiteSpace(result.VendorResultMessage)
            ? result.ActionOutcome
            : result.VendorResultMessage;
        var attemptsRemain = result.Retryable && claim.AttemptCount < claim.MaxAttempts;

        if (attemptsRemain)
        {
            return new FinalCommandState(
                RetryableStatus,
                NextAttemptAt: completedAt.Add(retryDelay),
                CompletedAt: completedAt,
                TerminalFailureAt: null,
                FailureCode: null,
                FailureReason: null,
                LastFailureCode: failureCode,
                LastFailureReason: failureReason);
        }

        return new FinalCommandState(
            TerminalFailureStatus,
            NextAttemptAt: null,
            CompletedAt: completedAt,
            TerminalFailureAt: completedAt,
            FailureCode: failureCode,
            FailureReason: failureReason,
            LastFailureCode: failureCode,
            LastFailureReason: failureReason);
    }

    private static string ComputeRequestHash(
        GateCommandExecutionClaim claim,
        HikCentralGateActionResult actionResult)
    {
        var builder = new StringBuilder();
        builder.Append("fake-hikcentral-gate-action:v1\n");
        builder.Append("gateCommandId=").Append(claim.CommandId.ToString("D", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("consumptionId=").Append(claim.GateAuthorizationConsumptionId.ToString("D", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("gateDeviceId=").Append(claim.GateDeviceId.ToString("D", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("vendorSystemId=").Append(claim.VendorSystemId.ToString("D", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("vendorOperation=").Append(actionResult.VendorOperation).Append('\n');
        builder.Append("targetResourceCode=").Append(actionResult.TargetResourceCode).Append('\n');
        builder.Append("requestMethod=").Append(actionResult.RequestMethod).Append('\n');
        builder.Append("requestPath=").Append(FakeRequestPath).Append('\n');
        builder.Append("correlationId=").Append(actionResult.RequestCorrelationId.ToString("D", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("requestedAt=").Append(actionResult.RequestedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static GateCommandClaimResult Rejected(
        string? commandStatus,
        string errorCode,
        string message,
        string? resultStatus) =>
        new(
            GateCommandClaimOutcome.Rejected,
            Claim: null,
            resultStatus ?? commandStatus,
            errorCode,
            message);

    private static Guid? GetNullableGuid(DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record CommandForClaim(
        Guid CommandId,
        string CommandType,
        Guid SourceProcessingId,
        Guid? SourceEventId,
        string? SourceEventRef,
        Guid GateAuthorizationConsumptionId,
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        Guid TariffSnapshotId,
        Guid? GateDeviceId,
        Guid? ServiceIdentityId,
        Guid? LaneId,
        Guid? SiteId,
        Guid? VendorSystemId,
        string CommandStatus,
        int AttemptCount,
        int MaxAttempts,
        string RetryPolicyCode,
        DateTimeOffset RequestedAt,
        DateTimeOffset? NextAttemptAt,
        Guid CorrelationId,
        string? TargetResourceCode,
        Guid? ExistingConsumptionId);

    private sealed record CommandFinalizationState(string CommandStatus, int AttemptCount);

    private sealed record FinalCommandState(
        string CommandStatus,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? TerminalFailureAt,
        string? FailureCode,
        string? FailureReason,
        string? LastFailureCode,
        string? LastFailureReason);
}
