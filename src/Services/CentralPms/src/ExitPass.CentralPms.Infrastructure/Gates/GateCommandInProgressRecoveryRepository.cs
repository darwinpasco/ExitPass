using System.Data;
using System.Data.Common;
using ExitPass.CentralPms.Application.Gates;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// PostgreSQL-backed recovery writer for stale IN_PROGRESS gate commands.
/// </summary>
public sealed class GateCommandInProgressRecoveryRepository : IGateCommandInProgressRecoveryRepository
{
    /// <summary>
    /// Stable failure code for interrupted command executions with no trustworthy vendor outcome.
    /// </summary>
    public const string AbandonedInProgressFailureCode = "ABANDONED_IN_PROGRESS";

    private const string InProgressStatus = "IN_PROGRESS";
    private const string RetryableStatus = "RETRYABLE";
    private const string TerminalFailureStatus = "TERMINAL_FAILURE";
    private const string RequestedStatus = "REQUESTED";
    private const string SucceededStatus = "SUCCEEDED";
    private const string RecoveryReason = "Gate command execution was interrupted or abandoned before a trustworthy vendor result was recorded.";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a gate command recovery repository.
    /// </summary>
    public GateCommandInProgressRecoveryRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateCommandRecoveryResult> RecoverAsync(
        GateCommandRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.GateCommandId == Guid.Empty)
        {
            return Rejected(request.GateCommandId, "GATE_COMMAND_ID_REQUIRED", "Gate command id is required.");
        }

        if (request.StaleBefore == default)
        {
            return Rejected(request.GateCommandId, "STALE_BEFORE_REQUIRED", "Stale-before timestamp is required.");
        }

        if (request.RetryDelay <= TimeSpan.Zero)
        {
            return Rejected(request.GateCommandId, "RETRY_DELAY_INVALID", "Retry delay must be positive.");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = await ReadForRecoveryAsync(
                connection,
                transaction,
                request.GateCommandId,
                cancellationToken);

            if (command is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Rejected(request.GateCommandId, "GATE_COMMAND_NOT_FOUND", "Gate command does not exist.");
            }

            var rejection = ValidateRecoveryEligibility(command, request);
            if (rejection is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return rejection;
            }

            var attemptsRemain = command.AttemptCount < command.MaxAttempts;
            var targetStatus = attemptsRemain ? RetryableStatus : TerminalFailureStatus;
            var nextAttemptAt = attemptsRemain ? request.RecoveredAt.Add(request.RetryDelay) : (DateTimeOffset?)null;
            var terminalFailureAt = attemptsRemain ? (DateTimeOffset?)null : request.RecoveredAt;

            var result = await UpdateRecoveredCommandAsync(
                connection,
                transaction,
                command,
                targetStatus,
                request.RecoveredAt,
                request.StaleBefore,
                nextAttemptAt,
                terminalFailureAt,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<RecoverableCommand?> ReadForRecoveryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                command_id,
                source_processing_id,
                gate_authorization_consumption_id,
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                tariff_snapshot_id,
                command_status,
                attempt_count,
                max_attempts,
                started_at,
                last_attempted_at,
                next_attempt_at,
                terminal_failure_at,
                failure_code,
                last_failure_code
            FROM gates.gate_commands
            WHERE command_id = @command_id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = commandId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RecoverableCommand(
            reader.GetGuid("command_id"),
            reader.GetGuid("source_processing_id"),
            reader.GetGuid("gate_authorization_consumption_id"),
            reader.GetGuid("exit_authorization_id"),
            reader.GetGuid("parking_session_id"),
            reader.GetGuid("payment_attempt_id"),
            reader.GetGuid("tariff_snapshot_id"),
            reader.GetString("command_status"),
            reader.GetInt32("attempt_count"),
            reader.GetInt32("max_attempts"),
            GetNullableDateTimeOffset(reader, "started_at"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_attempted_at")),
            GetNullableDateTimeOffset(reader, "next_attempt_at"),
            GetNullableDateTimeOffset(reader, "terminal_failure_at"),
            GetNullableString(reader, "failure_code"),
            GetNullableString(reader, "last_failure_code"));
    }

    private static GateCommandRecoveryResult? ValidateRecoveryEligibility(
        RecoverableCommand command,
        GateCommandRecoveryRequest request)
    {
        if (string.Equals(command.CommandStatus, RetryableStatus, StringComparison.Ordinal) ||
            string.Equals(command.CommandStatus, TerminalFailureStatus, StringComparison.Ordinal))
        {
            return new GateCommandRecoveryResult(
                command.CommandId,
                GateCommandRecoveryOutcome.AlreadyRecovered,
                command.CommandStatus,
                command.NextAttemptAt,
                command.TerminalFailureAt,
                Mutated: false,
                ErrorCode: null,
                Message: "Gate command already left IN_PROGRESS and was not changed.");
        }

        if (!string.Equals(command.CommandStatus, InProgressStatus, StringComparison.Ordinal))
        {
            return Rejected(
                command.CommandId,
                "GATE_COMMAND_STATUS_NOT_IN_PROGRESS",
                "Gate command must be IN_PROGRESS for stale recovery.",
                command.CommandStatus);
        }

        if (command.LastAttemptedAt > request.StaleBefore)
        {
            return Rejected(
                command.CommandId,
                "GATE_COMMAND_NOT_STALE",
                "Gate command IN_PROGRESS attempt is newer than the supplied stale cutoff.",
                command.CommandStatus);
        }

        if (command.AttemptCount < 0 ||
            command.MaxAttempts < 1 ||
            command.AttemptCount > command.MaxAttempts)
        {
            return Rejected(
                command.CommandId,
                "GATE_COMMAND_INVALID_ATTEMPT_STATE",
                "Gate command attempt counters are invalid.",
                command.CommandStatus);
        }

        if (command.SourceProcessingId == Guid.Empty ||
            command.GateAuthorizationConsumptionId == Guid.Empty ||
            command.ExitAuthorizationId == Guid.Empty ||
            command.ParkingSessionId == Guid.Empty ||
            command.PaymentAttemptId == Guid.Empty ||
            command.TariffSnapshotId == Guid.Empty)
        {
            return Rejected(
                command.CommandId,
                "GATE_COMMAND_REQUIRED_IDENTIFIERS_MISSING",
                "Gate command required identifiers are missing.",
                command.CommandStatus);
        }

        return null;
    }

    private static async Task<GateCommandRecoveryResult> UpdateRecoveredCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RecoverableCommand command,
        string targetStatus,
        DateTimeOffset recoveredAt,
        DateTimeOffset staleBefore,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset? terminalFailureAt,
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
              AND command_status = 'IN_PROGRESS'
              AND last_attempted_at <= @stale_before
              AND attempt_count = @attempt_count
              AND max_attempts = @max_attempts
            RETURNING command_status, next_attempt_at, terminal_failure_at;
            """;

        await using var update = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        update.Parameters.Add("command_id", NpgsqlDbType.Uuid).Value = command.CommandId;
        update.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = targetStatus;
        update.Parameters.Add("next_attempt_at", NpgsqlDbType.TimestampTz).Value = (object?)nextAttemptAt ?? DBNull.Value;
        update.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = recoveredAt;
        update.Parameters.Add("terminal_failure_at", NpgsqlDbType.TimestampTz).Value =
            (object?)terminalFailureAt ?? DBNull.Value;
        update.Parameters.Add("failure_code", NpgsqlDbType.Varchar).Value =
            targetStatus == TerminalFailureStatus ? AbandonedInProgressFailureCode : DBNull.Value;
        update.Parameters.Add("failure_reason", NpgsqlDbType.Text).Value =
            targetStatus == TerminalFailureStatus ? RecoveryReason : DBNull.Value;
        update.Parameters.Add("last_failure_code", NpgsqlDbType.Varchar).Value = AbandonedInProgressFailureCode;
        update.Parameters.Add("last_failure_reason", NpgsqlDbType.Text).Value = RecoveryReason;
        update.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = recoveredAt;
        update.Parameters.Add("stale_before", NpgsqlDbType.TimestampTz).Value = staleBefore;
        update.Parameters.Add("attempt_count", NpgsqlDbType.Integer).Value = command.AttemptCount;
        update.Parameters.Add("max_attempts", NpgsqlDbType.Integer).Value = command.MaxAttempts;

        await using var reader = await update.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Rejected(
                command.CommandId,
                "GATE_COMMAND_RECOVERY_CONFLICT",
                "Gate command state changed before stale recovery could be applied.",
                command.CommandStatus);
        }

        var commandStatus = reader.GetString("command_status");
        return new GateCommandRecoveryResult(
            command.CommandId,
            commandStatus == RetryableStatus
                ? GateCommandRecoveryOutcome.RecoveredRetryable
                : GateCommandRecoveryOutcome.RecoveredTerminalFailure,
            commandStatus,
            GetNullableDateTimeOffset(reader, "next_attempt_at"),
            GetNullableDateTimeOffset(reader, "terminal_failure_at"),
            Mutated: true,
            ErrorCode: null,
            Message: null);
    }

    private static GateCommandRecoveryResult Rejected(
        Guid gateCommandId,
        string errorCode,
        string message,
        string commandStatus = "") =>
        new(
            gateCommandId,
            GateCommandRecoveryOutcome.Rejected,
            commandStatus,
            NextAttemptAt: null,
            TerminalFailureAt: null,
            Mutated: false,
            errorCode,
            message);

    private static DateTimeOffset? GetNullableDateTimeOffset(DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string? GetNullableString(DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private sealed record RecoverableCommand(
        Guid CommandId,
        Guid SourceProcessingId,
        Guid GateAuthorizationConsumptionId,
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        Guid TariffSnapshotId,
        string CommandStatus,
        int AttemptCount,
        int MaxAttempts,
        DateTimeOffset? StartedAt,
        DateTimeOffset LastAttemptedAt,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? TerminalFailureAt,
        string? FailureCode,
        string? LastFailureCode);
}
