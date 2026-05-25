using ExitPass.CentralPms.Application.Eventing;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

/// <summary>
/// PostgreSQL repository for event dead-letter recovery and consumer checkpoint operations.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 9.7 Recommended Database Functions
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - Queries and updates are scoped to events-owned recovery tables only.
/// - Replay requests mark existing dead-letter state; they do not infer payment finality or settlement completion.
/// </summary>
public sealed class EventRecoveryRepository : IEventRecoveryRepository
{
    private readonly string _connectionString;

    public EventRecoveryRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<IReadOnlyList<DeadLetterRecord>> ListDeadLettersAsync(
        ListDeadLettersQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                dead_letter_record_id, outbox_event_id, event_publication_id, consumer_name,
                dead_letter_type::text AS dead_letter_type,
                dead_letter_status::text AS dead_letter_status,
                failure_reason_code, failure_detail_ref, payload_hash,
                dead_lettered_at, replay_requested_at, resolved_at, resolution_reason_code,
                correlation_id, created_at, updated_at
            FROM events.dead_letter_records
            WHERE (@status IS NULL OR dead_letter_status = @status::events.dead_letter_status_enum)
              AND (@consumer_name IS NULL OR consumer_name = @consumer_name)
            ORDER BY dead_lettered_at DESC, dead_letter_record_id DESC
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", query.Limit);
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = (object?)NormalizeBlank(query.Status) ?? DBNull.Value;
        command.Parameters.Add("consumer_name", NpgsqlDbType.Text).Value = (object?)NormalizeBlank(query.ConsumerName) ?? DBNull.Value;

        return await ReadDeadLettersAsync(command, cancellationToken);
    }

    public async Task<DeadLetterRecord?> GetDeadLetterAsync(
        GetDeadLetterQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                dead_letter_record_id, outbox_event_id, event_publication_id, consumer_name,
                dead_letter_type::text AS dead_letter_type,
                dead_letter_status::text AS dead_letter_status,
                failure_reason_code, failure_detail_ref, payload_hash,
                dead_lettered_at, replay_requested_at, resolved_at, resolution_reason_code,
                correlation_id, created_at, updated_at
            FROM events.dead_letter_records
            WHERE dead_letter_record_id = @dead_letter_record_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("dead_letter_record_id", query.DeadLetterRecordId);

        return (await ReadDeadLettersAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<DeadLetterRecord?> RequestDeadLetterReplayAsync(
        RequestDeadLetterReplayCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE events.dead_letter_records
               SET dead_letter_status = 'REPLAY_REQUESTED',
                   replay_requested_at = now(),
                   replay_requested_by_user_id = @requested_by_user_id,
                   replay_requested_by_service_identity_id = @requested_by_service_identity_id,
                   resolution_reason_code = COALESCE(@reason_code, resolution_reason_code),
                   correlation_id = COALESCE(@correlation_id, correlation_id),
                   updated_at = now(),
                   updated_by_user_id = @requested_by_user_id,
                   updated_by_service_identity_id = @requested_by_service_identity_id,
                   row_version = row_version + 1
             WHERE dead_letter_record_id = @dead_letter_record_id
               AND dead_letter_status IN ('OPEN', 'UNDER_REVIEW')
            RETURNING
                dead_letter_record_id, outbox_event_id, event_publication_id, consumer_name,
                dead_letter_type::text AS dead_letter_type,
                dead_letter_status::text AS dead_letter_status,
                failure_reason_code, failure_detail_ref, payload_hash,
                dead_lettered_at, replay_requested_at, resolved_at, resolution_reason_code,
                correlation_id, created_at, updated_at;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("dead_letter_record_id", command.DeadLetterRecordId);
        dbCommand.Parameters.Add("requested_by_user_id", NpgsqlDbType.Uuid).Value =
            (object?)command.RequestedByUserId ?? DBNull.Value;
        dbCommand.Parameters.Add("requested_by_service_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)command.RequestedByServiceIdentityId ?? DBNull.Value;
        dbCommand.Parameters.Add("reason_code", NpgsqlDbType.Text).Value =
            (object?)NormalizeBlank(command.ReasonCode) ?? DBNull.Value;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value =
            (object?)command.CorrelationId ?? DBNull.Value;

        return (await ReadDeadLettersAsync(dbCommand, cancellationToken)).SingleOrDefault();
    }

    public async Task<DeadLetterRecord?> MarkDeadLetterReplayOutcomeAsync(
        MarkDeadLetterReplayOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE events.dead_letter_records
               SET dead_letter_status = @outcome_status::events.dead_letter_status_enum,
                   resolved_at = now(),
                   resolved_by_user_id = @resolved_by_user_id,
                   resolved_by_service_identity_id = @resolved_by_service_identity_id,
                   resolution_reason_code = COALESCE(@reason_code, resolution_reason_code),
                   correlation_id = COALESCE(@correlation_id, correlation_id),
                   updated_at = now(),
                   updated_by_user_id = @resolved_by_user_id,
                   updated_by_service_identity_id = @resolved_by_service_identity_id,
                   row_version = row_version + 1
             WHERE dead_letter_record_id = @dead_letter_record_id
               AND dead_letter_status = 'REPLAY_REQUESTED'
            RETURNING
                dead_letter_record_id, outbox_event_id, event_publication_id, consumer_name,
                dead_letter_type::text AS dead_letter_type,
                dead_letter_status::text AS dead_letter_status,
                failure_reason_code, failure_detail_ref, payload_hash,
                dead_lettered_at, replay_requested_at, resolved_at, resolution_reason_code,
                correlation_id, created_at, updated_at;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("dead_letter_record_id", command.DeadLetterRecordId);
        dbCommand.Parameters.AddWithValue("outcome_status", command.OutcomeStatus);
        dbCommand.Parameters.Add("resolved_by_user_id", NpgsqlDbType.Uuid).Value =
            (object?)command.ResolvedByUserId ?? DBNull.Value;
        dbCommand.Parameters.Add("resolved_by_service_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)command.ResolvedByServiceIdentityId ?? DBNull.Value;
        dbCommand.Parameters.Add("reason_code", NpgsqlDbType.Text).Value =
            (object?)NormalizeBlank(command.ReasonCode) ?? DBNull.Value;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value =
            (object?)command.CorrelationId ?? DBNull.Value;

        return (await ReadDeadLettersAsync(dbCommand, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<ConsumerCheckpointRecord>> ListConsumerCheckpointsAsync(
        ListConsumerCheckpointsQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                consumer_checkpoint_id, consumer_name, consumer_group, subscription_name,
                event_type, aggregate_type, last_outbox_event_id, last_domain_event_id,
                last_broker_offset, checkpoint_status::text AS checkpoint_status,
                processed_count, failure_count, last_processed_at, last_failed_at,
                failure_reason_code, locked_at, locked_by_service_identity_id,
                updated_by_service_identity_id, created_at, updated_at, correlation_id
            FROM events.consumer_checkpoints
            WHERE (@status IS NULL OR checkpoint_status = @status::events.consumer_checkpoint_status_enum)
            ORDER BY updated_at DESC, consumer_name
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", query.Limit);
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = (object?)NormalizeBlank(query.Status) ?? DBNull.Value;

        return await ReadCheckpointsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ConsumerCheckpointRecord>> GetConsumerCheckpointAsync(
        GetConsumerCheckpointQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                consumer_checkpoint_id, consumer_name, consumer_group, subscription_name,
                event_type, aggregate_type, last_outbox_event_id, last_domain_event_id,
                last_broker_offset, checkpoint_status::text AS checkpoint_status,
                processed_count, failure_count, last_processed_at, last_failed_at,
                failure_reason_code, locked_at, locked_by_service_identity_id,
                updated_by_service_identity_id, created_at, updated_at, correlation_id
            FROM events.consumer_checkpoints
            WHERE consumer_name = @consumer_name
            ORDER BY updated_at DESC, consumer_checkpoint_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("consumer_name", query.ConsumerName);

        return await ReadCheckpointsAsync(command, cancellationToken);
    }

    public async Task<ConsumerCheckpointRecord?> UpdateConsumerCheckpointStatusAsync(
        UpdateConsumerCheckpointStatusCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE events.consumer_checkpoints
               SET checkpoint_status = @checkpoint_status::events.consumer_checkpoint_status_enum,
                   failure_reason_code = CASE
                       WHEN @checkpoint_status = 'FAILED' THEN COALESCE(@failure_reason_code, failure_reason_code, 'MANUAL_STATUS_UPDATE')
                       WHEN @checkpoint_status = 'ACTIVE' THEN NULL
                       ELSE COALESCE(@failure_reason_code, failure_reason_code)
                   END,
                   last_failed_at = CASE
                       WHEN @checkpoint_status = 'FAILED' THEN now()
                       ELSE last_failed_at
                   END,
                   locked_at = CASE
                       WHEN @checkpoint_status IN ('ACTIVE', 'PAUSED', 'FAILED', 'RETIRED') THEN NULL
                       ELSE locked_at
                   END,
                   locked_by_service_identity_id = CASE
                       WHEN @checkpoint_status IN ('ACTIVE', 'PAUSED', 'FAILED', 'RETIRED') THEN NULL
                       ELSE locked_by_service_identity_id
                   END,
                   correlation_id = COALESCE(@correlation_id, correlation_id),
                   updated_at = now(),
                   updated_by_service_identity_id = @updated_by_service_identity_id,
                   row_version = row_version + 1
             WHERE consumer_name = @consumer_name
               AND checkpoint_status <> 'RETIRED'
            RETURNING
                consumer_checkpoint_id, consumer_name, consumer_group, subscription_name,
                event_type, aggregate_type, last_outbox_event_id, last_domain_event_id,
                last_broker_offset, checkpoint_status::text AS checkpoint_status,
                processed_count, failure_count, last_processed_at, last_failed_at,
                failure_reason_code, locked_at, locked_by_service_identity_id,
                updated_by_service_identity_id, created_at, updated_at, correlation_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("consumer_name", command.ConsumerName);
        dbCommand.Parameters.AddWithValue("checkpoint_status", command.CheckpointStatus);
        dbCommand.Parameters.AddWithValue("updated_by_service_identity_id", command.UpdatedByServiceIdentityId);
        dbCommand.Parameters.Add("failure_reason_code", NpgsqlDbType.Text).Value =
            (object?)NormalizeBlank(command.FailureReasonCode) ?? DBNull.Value;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value =
            (object?)command.CorrelationId ?? DBNull.Value;

        return (await ReadCheckpointsAsync(dbCommand, cancellationToken)).SingleOrDefault();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<IReadOnlyList<DeadLetterRecord>> ReadDeadLettersAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var records = new List<DeadLetterRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new DeadLetterRecord(
                GetGuid(reader, "dead_letter_record_id"),
                GetNullableGuid(reader, "outbox_event_id"),
                GetNullableGuid(reader, "event_publication_id"),
                GetNullableString(reader, "consumer_name"),
                GetString(reader, "dead_letter_type"),
                GetString(reader, "dead_letter_status"),
                GetString(reader, "failure_reason_code"),
                GetNullableString(reader, "failure_detail_ref"),
                GetNullableString(reader, "payload_hash"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("dead_lettered_at")),
                GetNullableDateTimeOffset(reader, "replay_requested_at"),
                GetNullableDateTimeOffset(reader, "resolved_at"),
                GetNullableString(reader, "resolution_reason_code"),
                GetNullableGuid(reader, "correlation_id"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"))));
        }

        return records;
    }

    private static async Task<IReadOnlyList<ConsumerCheckpointRecord>> ReadCheckpointsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var records = new List<ConsumerCheckpointRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ConsumerCheckpointRecord(
                GetGuid(reader, "consumer_checkpoint_id"),
                GetString(reader, "consumer_name"),
                GetNullableString(reader, "consumer_group"),
                GetNullableString(reader, "subscription_name"),
                GetNullableString(reader, "event_type"),
                GetNullableString(reader, "aggregate_type"),
                GetNullableGuid(reader, "last_outbox_event_id"),
                GetNullableGuid(reader, "last_domain_event_id"),
                GetNullableString(reader, "last_broker_offset"),
                GetString(reader, "checkpoint_status"),
                GetInt64(reader, "processed_count"),
                GetInt64(reader, "failure_count"),
                GetNullableDateTimeOffset(reader, "last_processed_at"),
                GetNullableDateTimeOffset(reader, "last_failed_at"),
                GetNullableString(reader, "failure_reason_code"),
                GetNullableDateTimeOffset(reader, "locked_at"),
                GetNullableGuid(reader, "locked_by_service_identity_id"),
                GetGuid(reader, "updated_by_service_identity_id"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
                GetNullableGuid(reader, "correlation_id")));
        }

        return records;
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetString(NpgsqlDataReader reader, string columnName) =>
        reader.GetString(reader.GetOrdinal(columnName));

    private static Guid GetGuid(NpgsqlDataReader reader, string columnName) =>
        reader.GetGuid(reader.GetOrdinal(columnName));

    private static long GetInt64(NpgsqlDataReader reader, string columnName) =>
        reader.GetInt64(reader.GetOrdinal(columnName));

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
