using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Reconciliation;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

/// <summary>
/// PostgreSQL-backed dispatcher repository for reconciliation outbox records.
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
/// - Dispatch state is stored only in events-owned outbox/publication tables.
/// - Payment, provider, exit, gate, and settlement truth are never mutated by outbox dispatch.
/// </summary>
public sealed class ReconciliationOutboxDispatcherRepository : IReconciliationOutboxDispatcherRepository
{
    private const int RetryDelayMinutes = 5;

    internal const string ReconciliationPredicateSql = """
        (
            source_schema = 'reconciliation'
            OR routing_key LIKE 'central-pms.reconciliation.%'
        )
        """;

    internal const string ClaimSqlContainsForUpdateSkipLocked = "FOR UPDATE SKIP LOCKED";

    private readonly string _connectionString;
    private readonly ILogger<ReconciliationOutboxDispatcherRepository> _logger;

    /// <summary>
    /// Creates the repository.
    /// </summary>
    public ReconciliationOutboxDispatcherRepository(
        string connectionString,
        ILogger<ReconciliationOutboxDispatcherRepository> logger)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationOutboxPendingRecord>> ListPendingAsync(
        ListPendingReconciliationOutboxQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                outbox_event_id,
                event_type,
                aggregate_type,
                aggregate_id,
                publication_status::text AS publication_status,
                available_at,
                next_retry_at,
                retry_count,
                max_retry_count,
                correlation_id,
                causation_id
            FROM events.outbox_events
            WHERE (
                    source_schema = 'reconciliation'
                    OR routing_key LIKE 'central-pms.reconciliation.%'
                  )
              AND publication_status IN ('PENDING', 'RETRY_PENDING')
              AND available_at <= now()
              AND (next_retry_at IS NULL OR next_retry_at <= now())
            ORDER BY available_at, created_at, outbox_event_id
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", query.Limit);

        var pending = new List<ReconciliationOutboxPendingRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending.Add(new ReconciliationOutboxPendingRecord(
                reader.GetGuid("outbox_event_id"),
                reader.GetString("event_type"),
                reader.GetString("aggregate_type"),
                reader.GetGuid("aggregate_id"),
                reader.GetString("publication_status"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("available_at")),
                reader.GetNullableDateTimeOffset("next_retry_at"),
                reader.GetInt32("retry_count"),
                reader.GetInt32("max_retry_count"),
                reader.GetNullableGuid("correlation_id"),
                reader.GetNullableGuid("causation_id")));
        }

        return pending;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationOutboxEventRecord>> ClaimPendingAsync(
        DispatchReconciliationOutboxOnceCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH publisher AS (
                SELECT COALESCE(
                    @publisher_service_identity_id,
                    (
                        SELECT service_identity_id
                        FROM identity.service_identities
                        WHERE identity_status::text = 'ACTIVE'
                          AND service_identity_code IN ('central-pms', 'CENTRAL_PMS_API')
                        ORDER BY CASE service_identity_code WHEN 'central-pms' THEN 0 ELSE 1 END
                        LIMIT 1
                    )
                ) AS publisher_service_identity_id
            ),
            claimed AS (
                SELECT oe.*
                FROM events.outbox_events oe
                WHERE (
                        oe.source_schema = 'reconciliation'
                        OR oe.routing_key LIKE 'central-pms.reconciliation.%'
                      )
                  AND oe.publication_status IN ('PENDING', 'RETRY_PENDING')
                  AND oe.available_at <= now()
                  AND (oe.next_retry_at IS NULL OR oe.next_retry_at <= now())
                ORDER BY oe.available_at, oe.created_at, oe.outbox_event_id
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            ),
            updated AS (
                UPDATE events.outbox_events oe
                   SET publication_status = 'LOCKED',
                       locked_at = now(),
                       locked_by_service_identity_id = publisher.publisher_service_identity_id,
                       updated_at = now(),
                       updated_by_service_identity_id = publisher.publisher_service_identity_id,
                       row_version = oe.row_version + 1
                  FROM claimed, publisher
                 WHERE oe.outbox_event_id = claimed.outbox_event_id
                RETURNING
                    oe.outbox_event_id,
                    oe.domain_event_id,
                    oe.event_type,
                    oe.event_version,
                    oe.aggregate_type,
                    oe.aggregate_id,
                    oe.routing_key,
                    oe.exchange_name,
                    oe.payload_ref,
                    oe.payload_hash,
                    oe.payload_content_type,
                    oe.correlation_id,
                    oe.causation_id,
                    oe.retry_count,
                    oe.max_retry_count,
                    publisher.publisher_service_identity_id
            ),
            publication_attempts AS (
                SELECT
                    u.*,
                    COALESCE(
                        (
                            SELECT MAX(ep.publication_attempt_number) + 1
                            FROM events.event_publications ep
                            WHERE ep.outbox_event_id = u.outbox_event_id
                        ),
                        1
                    ) AS publication_attempt_number
                FROM updated u
            ),
            inserted_publications AS (
                INSERT INTO events.event_publications (
                    event_publication_id,
                    outbox_event_id,
                    publication_attempt_number,
                    publisher_service_identity_id,
                    broker_type,
                    exchange_name,
                    routing_key,
                    publication_status,
                    correlation_id,
                    started_at,
                    created_at
                )
                SELECT
                    gen_random_uuid(),
                    outbox_event_id,
                    publication_attempt_number,
                    publisher_service_identity_id,
                    'IN_PROCESS',
                    exchange_name,
                    routing_key,
                    'STARTED',
                    correlation_id,
                    now(),
                    now()
                FROM publication_attempts
                RETURNING
                    event_publication_id,
                    outbox_event_id,
                    publication_attempt_number
            )
            SELECT
                pa.outbox_event_id,
                pa.domain_event_id,
                ip.event_publication_id,
                ip.publication_attempt_number,
                pa.event_type,
                pa.event_version,
                pa.aggregate_type,
                pa.aggregate_id,
                pa.routing_key,
                pa.exchange_name,
                pa.payload_ref,
                pa.payload_hash,
                pa.payload_content_type,
                pa.correlation_id,
                pa.causation_id,
                pa.retry_count,
                pa.max_retry_count
            FROM publication_attempts pa
            JOIN inserted_publications ip ON ip.outbox_event_id = pa.outbox_event_id
            ORDER BY pa.outbox_event_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.Add("publisher_service_identity_id", NpgsqlDbType.Uuid).Value =
            (object?)command.PublisherServiceIdentityId ?? DBNull.Value;
        dbCommand.Parameters.AddWithValue("limit", command.Limit);

        var claimed = new List<ReconciliationOutboxEventRecord>();
        await using (var reader = await dbCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(ReadClaimedEvent(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    /// <inheritdoc />
    public async Task MarkPublishedAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        ReconciliationOutboxPublishOutcome outcome,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH publication AS (
                UPDATE events.event_publications ep
                   SET publication_status = 'PUBLISHED',
                       broker_message_id = @broker_message_id,
                       broker_acknowledged = true,
                       completed_at = now(),
                       duration_ms = GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (now() - ep.started_at)) * 1000)::int)
                 WHERE ep.event_publication_id = @event_publication_id
                   AND ep.outbox_event_id = @outbox_event_id
                RETURNING ep.publisher_service_identity_id
            )
            UPDATE events.outbox_events oe
               SET publication_status = 'PUBLISHED',
                   published_at = now(),
                   locked_at = NULL,
                   locked_by_service_identity_id = NULL,
                   failure_reason_code = NULL,
                   updated_at = now(),
                   updated_by_service_identity_id = publication.publisher_service_identity_id,
                   row_version = oe.row_version + 1
              FROM publication
             WHERE oe.outbox_event_id = @outbox_event_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("outbox_event_id", outboxEvent.OutboxEventId);
        dbCommand.Parameters.AddWithValue("event_publication_id", outboxEvent.EventPublicationId);
        dbCommand.Parameters.AddWithValue("broker_message_id", (object?)outcome.BrokerMessageId ?? DBNull.Value);

        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReconciliationOutboxDispatchItemResult> MarkFailedAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        ReconciliationOutboxPublishOutcome outcome,
        CancellationToken cancellationToken)
    {
        var failureReasonCode = string.IsNullOrWhiteSpace(outcome.FailureReasonCode)
            ? "PUBLICATION_FAILED"
            : outcome.FailureReasonCode;
        var willDeadLetter = outboxEvent.RetryCount + 1 >= outboxEvent.MaxRetryCount;
        var nextStatus = willDeadLetter ? "DEAD_LETTERED" : "RETRY_PENDING";

        const string sql = """
            WITH publication AS (
                UPDATE events.event_publications ep
                   SET publication_status = 'FAILED',
                       broker_acknowledged = false,
                       failure_reason_code = @failure_reason_code,
                       failure_detail_ref = @failure_detail_ref,
                       completed_at = now(),
                       duration_ms = GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (now() - ep.started_at)) * 1000)::int)
                 WHERE ep.event_publication_id = @event_publication_id
                   AND ep.outbox_event_id = @outbox_event_id
                RETURNING ep.publisher_service_identity_id
            ),
            updated_outbox AS (
                UPDATE events.outbox_events oe
                   SET publication_status = @next_publication_status::events.outbox_publication_status_enum,
                       locked_at = NULL,
                       locked_by_service_identity_id = NULL,
                       next_retry_at = CASE
                           WHEN @dead_letter THEN NULL
                           ELSE now() + (@retry_delay_minutes::text || ' minutes')::interval
                       END,
                       retry_count = oe.retry_count + 1,
                       failure_reason_code = @failure_reason_code,
                       updated_at = now(),
                       updated_by_service_identity_id = publication.publisher_service_identity_id,
                       row_version = oe.row_version + 1
                  FROM publication
                 WHERE oe.outbox_event_id = @outbox_event_id
                RETURNING
                    oe.outbox_event_id,
                    oe.payload_hash,
                    oe.correlation_id,
                    publication.publisher_service_identity_id
            ),
            dead_letter AS (
                INSERT INTO events.dead_letter_records (
                    dead_letter_record_id,
                    outbox_event_id,
                    event_publication_id,
                    dead_letter_type,
                    dead_letter_status,
                    failure_reason_code,
                    failure_detail_ref,
                    payload_hash,
                    dead_lettered_at,
                    correlation_id,
                    created_at,
                    created_by_service_identity_id,
                    updated_at,
                    updated_by_service_identity_id
                )
                SELECT
                    gen_random_uuid(),
                    outbox_event_id,
                    @event_publication_id,
                    'RETRY_EXHAUSTED',
                    'OPEN',
                    @failure_reason_code,
                    @failure_detail_ref,
                    payload_hash,
                    now(),
                    correlation_id,
                    now(),
                    publisher_service_identity_id,
                    now(),
                    publisher_service_identity_id
                FROM updated_outbox
                WHERE @dead_letter
                RETURNING dead_letter_record_id
            )
            SELECT COUNT(*) FROM updated_outbox;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("outbox_event_id", outboxEvent.OutboxEventId);
        dbCommand.Parameters.AddWithValue("event_publication_id", outboxEvent.EventPublicationId);
        dbCommand.Parameters.AddWithValue("failure_reason_code", failureReasonCode);
        dbCommand.Parameters.AddWithValue("failure_detail_ref", (object?)outcome.FailureDetailRef ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("next_publication_status", nextStatus);
        dbCommand.Parameters.AddWithValue("dead_letter", willDeadLetter);
        dbCommand.Parameters.AddWithValue("retry_delay_minutes", RetryDelayMinutes);

        var updated = await dbCommand.ExecuteScalarAsync(cancellationToken);
        if (updated is long count && count == 0)
        {
            _logger.LogWarning(
                "Reconciliation outbox failure update did not affect a row. outbox_event_id={OutboxEventId} publication_id={EventPublicationId}",
                outboxEvent.OutboxEventId,
                outboxEvent.EventPublicationId);
        }

        return new ReconciliationOutboxDispatchItemResult(
            outboxEvent.OutboxEventId,
            outboxEvent.EventPublicationId,
            outboxEvent.EventType,
            Succeeded: false,
            nextStatus,
            failureReasonCode,
            outcome.BrokerMessageId);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static ReconciliationOutboxEventRecord ReadClaimedEvent(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid("outbox_event_id"),
            reader.GetNullableGuid("domain_event_id"),
            reader.GetGuid("event_publication_id"),
            reader.GetInt32("publication_attempt_number"),
            reader.GetString("event_type"),
            reader.GetInt32("event_version"),
            reader.GetString("aggregate_type"),
            reader.GetGuid("aggregate_id"),
            reader.GetString("routing_key"),
            reader.GetNullableString("exchange_name"),
            reader.GetNullableString("payload_ref"),
            reader.GetNullableString("payload_hash"),
            reader.GetString("payload_content_type"),
            reader.GetNullableGuid("correlation_id"),
            reader.GetNullableGuid("causation_id"),
            reader.GetInt32("retry_count"),
            reader.GetInt32("max_retry_count"));
}
