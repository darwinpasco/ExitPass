using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

internal sealed class DurableIntegrationEventPublisher : IIntegrationEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;
    private readonly IIntegrationEventPublisher _innerPublisher;
    private readonly CentralPmsMetrics _metrics;

    public DurableIntegrationEventPublisher(
        string connectionString,
        IIntegrationEventPublisher innerPublisher,
        CentralPmsMetrics metrics)
    {
        _connectionString = connectionString;
        _innerPublisher = innerPublisher;
        _metrics = metrics;
    }

    public async Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            await PersistAsync(envelope, cancellationToken);
            _metrics.DurableEventPersistenceOutcome(envelope.EventType, "SUCCESS");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _metrics.DurableEventPersistenceOutcome(envelope.EventType, "FAILURE", exception.GetType().Name);
            throw;
        }

        await _innerPublisher.PublishAsync(envelope, cancellationToken);
    }

    private async Task PersistAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(envelope.AggregateId, out var aggregateId))
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();
        var payloadRef = $"central-pms://integration-events/{envelope.EventId}";
        var sourceTable = ResolveSourceTable(envelope.AggregateType);
        var occurredAt = envelope.OccurredAtUtc == default ? DateTimeOffset.UtcNow : envelope.OccurredAtUtc;
        var domainEventId = envelope.EventId;
        var outboxEventId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO events.domain_events (
                    domain_event_id,
                    source_schema,
                    source_table,
                    event_type,
                    event_version,
                    aggregate_type,
                    aggregate_id,
                    event_status,
                    payload_ref,
                    payload_hash,
                    occurred_at,
                    recorded_at,
                    correlation_id,
                    causation_id,
                    created_at
                )
                VALUES (
                    @domain_event_id,
                    'central_pms',
                    @source_table,
                    @event_type,
                    @event_version,
                    @aggregate_type,
                    @aggregate_id,
                    'RECORDED',
                    @payload_ref,
                    @payload_hash,
                    @occurred_at,
                    @recorded_at,
                    @correlation_id,
                    @causation_id,
                    @recorded_at
                );

                INSERT INTO events.outbox_events (
                    outbox_event_id,
                    domain_event_id,
                    source_schema,
                    source_table,
                    event_type,
                    event_version,
                    aggregate_type,
                    aggregate_id,
                    routing_key,
                    exchange_name,
                    payload_ref,
                    payload_hash,
                    payload_content_type,
                    publication_status,
                    available_at,
                    retry_count,
                    max_retry_count,
                    correlation_id,
                    causation_id,
                    created_at,
                    updated_at
                )
                VALUES (
                    @outbox_event_id,
                    @domain_event_id,
                    'central_pms',
                    @source_table,
                    @event_type,
                    @event_version,
                    @aggregate_type,
                    @aggregate_id,
                    @routing_key,
                    'exitpass.central-pms',
                    @payload_ref,
                    @payload_hash,
                    'application/json',
                    'PENDING',
                    @recorded_at,
                    0,
                    10,
                    @correlation_id,
                    @causation_id,
                    @recorded_at,
                    @recorded_at
                );

                INSERT INTO audit.audit_events (
                    audit_event_id,
                    event_type,
                    event_category,
                    event_result,
                    target_entity_type,
                    target_entity_id,
                    source_schema,
                    source_service_name,
                    source_channel,
                    summary,
                    details_ref,
                    details_hash,
                    occurred_at,
                    recorded_at,
                    correlation_id,
                    causation_id,
                    created_at
                )
                VALUES (
                    @audit_event_id,
                    @event_type,
                    'DOMAIN_STATE_CHANGE',
                    @audit_result::audit.audit_event_result_enum,
                    @aggregate_type,
                    @aggregate_id,
                    'central_pms',
                    'ExitPass.CentralPms',
                    'APPLICATION',
                    @summary,
                    @payload_ref,
                    @payload_hash,
                    @occurred_at,
                    @recorded_at,
                    @correlation_id,
                    @causation_id,
                    @recorded_at
                );
                """;

            command.Parameters.AddWithValue("domain_event_id", domainEventId);
            command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
            command.Parameters.AddWithValue("audit_event_id", Guid.NewGuid());
            command.Parameters.AddWithValue("source_table", (object?)sourceTable ?? DBNull.Value);
            command.Parameters.AddWithValue("event_type", envelope.EventType);
            command.Parameters.AddWithValue("event_version", envelope.SchemaVersion);
            command.Parameters.AddWithValue("aggregate_type", envelope.AggregateType);
            command.Parameters.AddWithValue("aggregate_id", aggregateId);
            command.Parameters.AddWithValue("payload_ref", payloadRef);
            command.Parameters.AddWithValue("payload_hash", payloadHash);
            command.Parameters.AddWithValue("occurred_at", occurredAt);
            command.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("correlation_id", envelope.CorrelationId);
            command.Parameters.AddWithValue("causation_id", (object?)envelope.CausationId ?? DBNull.Value);
            command.Parameters.AddWithValue("routing_key", $"central-pms.{envelope.EventType}");
            command.Parameters.AddWithValue("audit_result", ResolveAuditResult(envelope.EventType));
            command.Parameters.AddWithValue("summary", $"Central PMS recorded {envelope.EventType}.");

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (string.Equals(envelope.EventType, IntegrationEventTypes.GateAuthorizationConsumed, StringComparison.Ordinal))
        {
            await PersistGateConsumedEventAsync(connection, transaction, aggregateId, envelope, occurredAt, cancellationToken);
        }
        else if (string.Equals(envelope.EventType, IntegrationEventTypes.DuplicateGateConsumeRejected, StringComparison.Ordinal))
        {
            await PersistDuplicateGateConsumeRejectedEventAsync(
                connection,
                transaction,
                aggregateId,
                envelope,
                occurredAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task PersistGateConsumedEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid exitAuthorizationId,
        IntegrationEventEnvelope envelope,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO gates.gate_events (
                gate_event_id,
                gate_authorization_consumption_id,
                exit_authorization_id,
                gate_device_id,
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
                @gate_event_id,
                gac.gate_authorization_consumption_id,
                ea.exit_authorization_id,
                gac.gate_device_id,
                ps.site_id,
                gac.lane_id,
                'AUTHORIZATION_CONSUMED',
                'SUCCESS',
                'EXIT_AUTHORIZATION_CONSUMED',
                @payload_ref,
                @payload_hash,
                @source_event_ref,
                @occurred_at,
                @received_at,
                @correlation_id,
                @received_at,
                COALESCE(gac.created_by_service_identity_id, ea.created_by_service_identity_id)
            FROM core.exit_authorizations ea
            JOIN core.parking_sessions ps
              ON ps.parking_session_id = ea.parking_session_id
            JOIN gates.gate_authorization_consumptions gac
              ON gac.exit_authorization_id = ea.exit_authorization_id
             AND gac.consume_status = 'CONSUMED'
            WHERE ea.exit_authorization_id = @exit_authorization_id
            ORDER BY gac.consumed_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("gate_event_id", Guid.NewGuid());
        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("received_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("correlation_id", envelope.CorrelationId);
        command.Parameters.AddWithValue("payload_ref", $"central-pms://integration-events/{envelope.EventId}");
        command.Parameters.AddWithValue(
            "payload_hash",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOptions))))
                .ToLowerInvariant());
        command.Parameters.AddWithValue("source_event_ref", envelope.EventId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PersistDuplicateGateConsumeRejectedEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid exitAuthorizationId,
        IntegrationEventEnvelope envelope,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO gates.gate_events (
                gate_event_id,
                exit_authorization_id,
                site_id,
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
                @gate_event_id,
                ea.exit_authorization_id,
                ps.site_id,
                'AUTHORIZATION_DENIED',
                'DUPLICATE',
                'EXIT_AUTHORIZATION_ALREADY_CONSUMED',
                @occurred_at,
                @received_at,
                @correlation_id,
                @received_at,
                COALESCE(ea.updated_by_service_identity_id, ea.created_by_service_identity_id)
            FROM core.exit_authorizations ea
            JOIN core.parking_sessions ps
              ON ps.parking_session_id = ea.parking_session_id
            WHERE ea.exit_authorization_id = @exit_authorization_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("gate_event_id", Guid.NewGuid());
        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("received_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("correlation_id", envelope.CorrelationId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? ResolveSourceTable(string aggregateType)
    {
        return aggregateType switch
        {
            "PaymentAttempt" => "payment_attempts",
            "PaymentConfirmation" => "payment_confirmations",
            "ExitAuthorization" => "exit_authorizations",
            _ => null
        };
    }

    private static string ResolveAuditResult(string eventType)
    {
        return eventType.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ? "DUPLICATE" : "SUCCESS";
    }
}
