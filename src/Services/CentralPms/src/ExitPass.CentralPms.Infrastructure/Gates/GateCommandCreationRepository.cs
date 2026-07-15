using System.Data;
using ExitPass.CentralPms.Application.Gates;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Gates;

/// <summary>
/// PostgreSQL-backed writer for canonical consumed-processing and vendor-neutral gate command records.
/// </summary>
public sealed class GateCommandCreationRepository : IGateCommandCreationRepository
{
    private const string ProcessedResultCode = "COMMAND_REQUESTED";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a gate command creation repository.
    /// </summary>
    public GateCommandCreationRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateCommandCreationResult> CreateOrReuseAsync(
        GateCommandCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var consumption = await ReadConsumptionAsync(connection, transaction, request, cancellationToken);
            ValidateConsumption(request, consumption);

            var resolved = ResolveRequestFacts(request, consumption);
            var insertedProcessing = await TryInsertProcessingAsync(connection, transaction, resolved, cancellationToken);
            if (insertedProcessing is null)
            {
                var existingProcessing = await ReadExistingProcessingAsync(
                    connection,
                    transaction,
                    resolved.ProcessingKey,
                    resolved.EventType,
                    cancellationToken);

                if (existingProcessing is null)
                {
                    throw new GateCommandCreationRejectedException(
                        "GATE_COMMAND_PROCESSING_NOT_FOUND",
                        "Existing consumed-processing row could not be read after idempotency conflict.");
                }

                ValidateProcessingReplay(resolved, existingProcessing);
                var existingCommand = await ReadExistingCommandAsync(
                    connection,
                    transaction,
                    existingProcessing.ProcessingId,
                    resolved.CommandType,
                    cancellationToken);

                if (existingCommand is null)
                {
                    throw new GateCommandCreationRejectedException(
                        "GATE_COMMAND_REPLAY_INCOMPLETE",
                        "Existing consumed-processing row does not have a matching gate command.");
                }

                ValidateCommandReplay(resolved, existingProcessing, existingCommand);
                await transaction.CommitAsync(cancellationToken);

                return new GateCommandCreationResult(
                    existingProcessing.ProcessingId,
                    existingProcessing.ProcessingKey,
                    existingCommand.CommandId,
                    existingCommand.CommandType,
                    GateCommandCreationOutcome.IdempotentReplay);
            }

            var command = await InsertCommandAsync(
                connection,
                transaction,
                resolved,
                insertedProcessing.ProcessingId,
                cancellationToken);

            await MarkProcessingCompletedAsync(
                connection,
                transaction,
                insertedProcessing.ProcessingId,
                resolved.RequestedAt,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new GateCommandCreationResult(
                insertedProcessing.ProcessingId,
                insertedProcessing.ProcessingKey,
                command.CommandId,
                command.CommandType,
                GateCommandCreationOutcome.Created);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new GateCommandCreationRejectedException(
                "GATE_COMMAND_CREATION_CONFLICT",
                "A consumed-processing or gate command uniqueness constraint rejected a conflicting event replay.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ConsumptionFacts> ReadConsumptionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GateCommandCreationRequest request,
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
                correlation_id,
                created_by_service_identity_id
            FROM gates.gate_authorization_consumptions
            WHERE gate_authorization_consumption_id = @gate_authorization_consumption_id
            FOR SHARE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value =
            request.GateAuthorizationConsumptionId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new GateCommandCreationRejectedException(
                "GATE_AUTHORIZATION_CONSUMPTION_NOT_FOUND",
                "Gate authorization consumption row must exist before command creation.");
        }

        return new ConsumptionFacts(
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            GetNullableGuid(reader, "exit_authorization_id"),
            GetNullableGuid(reader, "gate_device_id"),
            reader.GetGuid(reader.GetOrdinal("site_id")),
            GetNullableGuid(reader, "lane_id"),
            reader.GetString(reader.GetOrdinal("consume_status")),
            GetNullableDateTimeOffset(reader, "consumed_at"),
            GetNullableGuid(reader, "correlation_id"),
            GetNullableGuid(reader, "created_by_service_identity_id"));
    }

    private static void ValidateConsumption(GateCommandCreationRequest request, ConsumptionFacts consumption)
    {
        if (!string.Equals(consumption.ConsumeStatus, "CONSUMED", StringComparison.Ordinal))
        {
            throw Conflict("Gate authorization consumption is not in CONSUMED status.");
        }

        if (consumption.ExitAuthorizationId != request.ExitAuthorizationId)
        {
            throw Conflict("Gate authorization consumption exit authorization does not match the event.");
        }

        if (request.GateDeviceId.HasValue &&
            consumption.GateDeviceId.HasValue &&
            request.GateDeviceId.Value != consumption.GateDeviceId.Value)
        {
            throw Conflict("Gate authorization consumption gate device does not match the event.");
        }

        if (request.SiteId.HasValue && request.SiteId.Value != consumption.SiteId)
        {
            throw Conflict("Gate authorization consumption site does not match the event.");
        }

        if (request.LaneId.HasValue &&
            consumption.LaneId.HasValue &&
            request.LaneId.Value != consumption.LaneId.Value)
        {
            throw Conflict("Gate authorization consumption lane does not match the event.");
        }
    }

    private static ResolvedGateCommandCreationRequest ResolveRequestFacts(
        GateCommandCreationRequest request,
        ConsumptionFacts consumption)
    {
        return new ResolvedGateCommandCreationRequest(
            request.EventId,
            request.EventType,
            request.EventRef,
            request.ProcessingKey,
            request.GateAuthorizationConsumptionId,
            request.ExitAuthorizationId,
            request.ParkingSessionId,
            request.PaymentAttemptId,
            request.TariffSnapshotId,
            request.GateDeviceId ?? consumption.GateDeviceId,
            request.ServiceIdentityId ?? consumption.CreatedByServiceIdentityId,
            request.LaneId ?? consumption.LaneId,
            request.SiteId ?? consumption.SiteId,
            request.VendorSystemId,
            request.ConsumedAt,
            request.CorrelationId,
            request.CommandType,
            request.RequestedAt);
    }

    private static async Task<ProcessingFacts?> TryInsertProcessingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResolvedGateCommandCreationRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO gates.gate_authorization_consumed_processing (
                processing_key,
                event_id,
                event_type,
                event_ref,
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
                consumed_at,
                correlation_id,
                processing_status,
                processing_result,
                attempt_count,
                first_attempted_at,
                last_attempted_at,
                processed_at,
                failure_code,
                failure_reason,
                created_at,
                updated_at
            )
            VALUES (
                @processing_key,
                @event_id,
                @event_type,
                @event_ref,
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
                @consumed_at,
                @correlation_id,
                'PROCESSING',
                'COMMAND_CREATION_STARTED',
                1,
                @now,
                @now,
                NULL,
                NULL,
                NULL,
                @now,
                @now
            )
            ON CONFLICT (processing_key, event_type) DO NOTHING
            RETURNING
                processing_id,
                processing_key,
                event_id,
                event_type,
                event_ref,
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
                consumed_at,
                correlation_id,
                processing_status,
                processing_result;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddRequestParameters(command, request);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadProcessing(reader)
            : null;
    }

    private static async Task<ProcessingFacts?> ReadExistingProcessingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processingKey,
        string eventType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                processing_id,
                processing_key,
                event_id,
                event_type,
                event_ref,
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
                consumed_at,
                correlation_id,
                processing_status,
                processing_result
            FROM gates.gate_authorization_consumed_processing
            WHERE processing_key = @processing_key
              AND event_type = @event_type
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = processingKey;
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = eventType;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadProcessing(reader)
            : null;
    }

    private static void ValidateProcessingReplay(
        ResolvedGateCommandCreationRequest request,
        ProcessingFacts processing)
    {
        if (processing.EventId != request.EventId ||
            !string.Equals(processing.EventType, request.EventType, StringComparison.Ordinal) ||
            processing.GateAuthorizationConsumptionId != request.GateAuthorizationConsumptionId ||
            processing.ExitAuthorizationId != request.ExitAuthorizationId ||
            processing.ParkingSessionId != request.ParkingSessionId ||
            processing.PaymentAttemptId != request.PaymentAttemptId ||
            processing.TariffSnapshotId != request.TariffSnapshotId ||
            processing.GateDeviceId != request.GateDeviceId ||
            processing.LaneId != request.LaneId ||
            processing.SiteId != request.SiteId ||
            processing.VendorSystemId != request.VendorSystemId)
        {
            throw Conflict("Existing consumed-processing row does not match the incoming event.");
        }

        if (!string.Equals(processing.ProcessingStatus, "PROCESSED", StringComparison.Ordinal) ||
            !string.Equals(processing.ProcessingResult, ProcessedResultCode, StringComparison.Ordinal))
        {
            throw new GateCommandCreationRejectedException(
                "GATE_COMMAND_REPLAY_INCOMPLETE",
                "Existing consumed-processing row is not a completed command-creation result.");
        }
    }

    private static async Task<CommandFacts> InsertCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResolvedGateCommandCreationRequest request,
        Guid processingId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO gates.gate_commands (
                command_type,
                source_processing_id,
                source_event_id,
                source_event_ref,
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
                last_failure_reason,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @command_type,
                @source_processing_id,
                @event_id,
                @event_ref,
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
                'REQUESTED',
                0,
                3,
                'GATE_COMMAND_RETRY_V1',
                @now,
                NULL,
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
            RETURNING command_id, command_type, source_processing_id, gate_authorization_consumption_id, command_status;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddRequestParameters(command, request);
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = processingId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new GateCommandCreationRejectedException(
                "GATE_COMMAND_NOT_CREATED",
                "Gate command insert did not return a command row.");
        }

        return new CommandFacts(
            reader.GetGuid(reader.GetOrdinal("command_id")),
            reader.GetString(reader.GetOrdinal("command_type")),
            reader.GetGuid(reader.GetOrdinal("source_processing_id")),
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetString(reader.GetOrdinal("command_status")));
    }

    private static async Task MarkProcessingCompletedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid processingId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE gates.gate_authorization_consumed_processing
            SET
                processing_status = 'PROCESSED',
                processing_result = @processing_result,
                processed_at = @processed_at,
                updated_at = @processed_at
            WHERE processing_id = @processing_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("processing_id", NpgsqlDbType.Uuid).Value = processingId;
        command.Parameters.Add("processing_result", NpgsqlDbType.Varchar).Value = ProcessedResultCode;
        command.Parameters.Add("processed_at", NpgsqlDbType.TimestampTz).Value = completedAt;

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new GateCommandCreationRejectedException(
                "GATE_COMMAND_PROCESSING_NOT_COMPLETED",
                "Consumed-processing row could not be marked as processed.");
        }
    }

    private static async Task<CommandFacts?> ReadExistingCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sourceProcessingId,
        string commandType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                command_id,
                command_type,
                source_processing_id,
                gate_authorization_consumption_id,
                command_status
            FROM gates.gate_commands
            WHERE source_processing_id = @source_processing_id
              AND command_type = @command_type
            FOR SHARE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("source_processing_id", NpgsqlDbType.Uuid).Value = sourceProcessingId;
        command.Parameters.Add("command_type", NpgsqlDbType.Varchar).Value = commandType;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CommandFacts(
            reader.GetGuid(reader.GetOrdinal("command_id")),
            reader.GetString(reader.GetOrdinal("command_type")),
            reader.GetGuid(reader.GetOrdinal("source_processing_id")),
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetString(reader.GetOrdinal("command_status")));
    }

    private static void ValidateCommandReplay(
        ResolvedGateCommandCreationRequest request,
        ProcessingFacts processing,
        CommandFacts command)
    {
        if (command.SourceProcessingId != processing.ProcessingId ||
            command.GateAuthorizationConsumptionId != request.GateAuthorizationConsumptionId ||
            !string.Equals(command.CommandType, request.CommandType, StringComparison.Ordinal))
        {
            throw Conflict("Existing gate command does not match the incoming event.");
        }
    }

    private static ProcessingFacts ReadProcessing(NpgsqlDataReader reader)
    {
        return new ProcessingFacts(
            reader.GetGuid(reader.GetOrdinal("processing_id")),
            reader.GetGuid(reader.GetOrdinal("processing_key")),
            GetNullableGuid(reader, "event_id"),
            reader.GetString(reader.GetOrdinal("event_type")),
            GetNullableString(reader, "event_ref"),
            reader.GetGuid(reader.GetOrdinal("gate_authorization_consumption_id")),
            reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            GetNullableGuid(reader, "gate_device_id"),
            GetNullableGuid(reader, "service_identity_id"),
            GetNullableGuid(reader, "lane_id"),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "vendor_system_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("consumed_at")),
            reader.GetGuid(reader.GetOrdinal("correlation_id")),
            reader.GetString(reader.GetOrdinal("processing_status")),
            reader.GetString(reader.GetOrdinal("processing_result")));
    }

    private static void AddRequestParameters(NpgsqlCommand command, ResolvedGateCommandCreationRequest request)
    {
        command.Parameters.Add("processing_key", NpgsqlDbType.Uuid).Value = request.ProcessingKey;
        command.Parameters.Add("event_id", NpgsqlDbType.Uuid).Value = request.EventId;
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = request.EventType;
        command.Parameters.Add("event_ref", NpgsqlDbType.Varchar).Value = request.EventRef;
        command.Parameters.Add("gate_authorization_consumption_id", NpgsqlDbType.Uuid).Value = request.GateAuthorizationConsumptionId;
        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = request.ExitAuthorizationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = request.ParkingSessionId;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = request.PaymentAttemptId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = request.TariffSnapshotId;
        command.Parameters.Add("gate_device_id", NpgsqlDbType.Uuid).Value = (object?)request.GateDeviceId ?? DBNull.Value;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)request.ServiceIdentityId ?? DBNull.Value;
        command.Parameters.Add("lane_id", NpgsqlDbType.Uuid).Value = (object?)request.LaneId ?? DBNull.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = (object?)request.SiteId ?? DBNull.Value;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = (object?)request.VendorSystemId ?? DBNull.Value;
        command.Parameters.Add("consumed_at", NpgsqlDbType.TimestampTz).Value = request.ConsumedAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = request.CorrelationId;
        command.Parameters.Add("command_type", NpgsqlDbType.Varchar).Value = request.CommandType;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = request.RequestedAt;
    }

    private static GateCommandCreationRejectedException Conflict(string message) =>
        new("GATE_COMMAND_CREATION_CONFLICT", message);

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record ConsumptionFacts(
        Guid GateAuthorizationConsumptionId,
        Guid? ExitAuthorizationId,
        Guid? GateDeviceId,
        Guid SiteId,
        Guid? LaneId,
        string ConsumeStatus,
        DateTimeOffset? ConsumedAt,
        Guid? CorrelationId,
        Guid? CreatedByServiceIdentityId);

    private sealed record ResolvedGateCommandCreationRequest(
        Guid EventId,
        string EventType,
        string EventRef,
        Guid ProcessingKey,
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
        DateTimeOffset ConsumedAt,
        Guid CorrelationId,
        string CommandType,
        DateTimeOffset RequestedAt);

    private sealed record ProcessingFacts(
        Guid ProcessingId,
        Guid ProcessingKey,
        Guid? EventId,
        string EventType,
        string? EventRef,
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
        DateTimeOffset ConsumedAt,
        Guid CorrelationId,
        string ProcessingStatus,
        string ProcessingResult);

    private sealed record CommandFacts(
        Guid CommandId,
        string CommandType,
        Guid SourceProcessingId,
        Guid GateAuthorizationConsumptionId,
        string CommandStatus);
}
