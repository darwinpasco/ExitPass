using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Reconciliation;

/// <summary>
/// Persists reconciliation audit, domain, and outbox evidence using the live v1.2 audit/events schema.
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
/// - Event evidence is operational traceability only and never mutates payment, provider, exit, gate, or settlement truth.
/// - Reconciliation state remains authoritative in reconciliation-owned tables.
/// </summary>
internal static class ReconciliationEventPersistence
{
    public const string MopsTransactionImported = "ReconciliationMopsTransactionImported";
    public const string ReconciliationRunCreated = "ReconciliationRunCreated";
    public const string ReconciliationItemEvaluated = "ReconciliationItemEvaluated";
    public const string ReconciliationRunEvaluated = "ReconciliationRunEvaluated";
    public const string ReconciliationExceptionLifecycleChanged = "ReconciliationExceptionLifecycleChanged";
    public const string ReconciliationResolutionRequestSubmitted = "ReconciliationResolutionRequestSubmitted";
    public const string ReconciliationResolutionDecisionRecorded = "ReconciliationResolutionDecisionRecorded";
    public const string ReconciliationNoteAdded = "ReconciliationNoteAdded";

    /// <summary>
    /// Inserts audit/domain/outbox evidence for a reconciliation action.
    /// </summary>
    public static async Task PersistAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string eventType,
        string sourceTable,
        string aggregateType,
        Guid aggregateId,
        Guid? relatedEntityId,
        Guid? actorUserId,
        Guid? actorServiceIdentityId,
        Guid? correlationId,
        Guid? causationId,
        string summary,
        CancellationToken cancellationToken)
    {
        var domainEventId = Guid.NewGuid();
        var outboxEventId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var recordedAt = DateTimeOffset.UtcNow;
        var payloadRef = $"central-pms://reconciliation-events/{domainEventId}";

        await using var command = connection.CreateCommand();
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
                related_entity_type,
                related_entity_id,
                event_status,
                payload_ref,
                occurred_at,
                recorded_at,
                actor_user_id,
                actor_service_identity_id,
                correlation_id,
                causation_id,
                created_at,
                created_by_service_identity_id
            )
            VALUES (
                @domain_event_id,
                'reconciliation',
                @source_table,
                @event_type,
                1,
                @aggregate_type,
                @aggregate_id,
                @related_entity_type,
                @related_entity_id,
                'RECORDED',
                @payload_ref,
                @recorded_at,
                @recorded_at,
                @actor_user_id,
                @actor_service_identity_id,
                @correlation_id,
                @causation_id,
                @recorded_at,
                @actor_service_identity_id
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
                payload_content_type,
                publication_status,
                available_at,
                retry_count,
                max_retry_count,
                correlation_id,
                causation_id,
                created_at,
                updated_at,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @outbox_event_id,
                @domain_event_id,
                'reconciliation',
                @source_table,
                @event_type,
                1,
                @aggregate_type,
                @aggregate_id,
                @routing_key,
                'exitpass.central-pms',
                @payload_ref,
                'application/json',
                'PENDING',
                @recorded_at,
                0,
                10,
                @correlation_id,
                @causation_id,
                @recorded_at,
                @recorded_at,
                @actor_service_identity_id,
                @actor_service_identity_id
            );

            INSERT INTO audit.audit_events (
                audit_event_id,
                event_type,
                event_category,
                event_result,
                target_entity_type,
                target_entity_id,
                related_entity_type,
                related_entity_id,
                source_schema,
                source_service_name,
                source_channel,
                actor_user_id,
                actor_service_identity_id,
                summary,
                details_ref,
                occurred_at,
                recorded_at,
                correlation_id,
                causation_id,
                created_at,
                created_by_service_identity_id
            )
            VALUES (
                @audit_event_id,
                @event_type,
                'RECONCILIATION',
                'SUCCESS',
                @aggregate_type,
                @aggregate_id,
                @related_entity_type,
                @related_entity_id,
                'reconciliation',
                'ExitPass.CentralPms',
                'APPLICATION',
                @actor_user_id,
                @actor_service_identity_id,
                @summary,
                @payload_ref,
                @recorded_at,
                @recorded_at,
                @correlation_id,
                @causation_id,
                @recorded_at,
                @actor_service_identity_id
            );
            """;

        command.Parameters.AddWithValue("domain_event_id", domainEventId);
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("audit_event_id", auditEventId);
        command.Parameters.AddWithValue("source_table", sourceTable);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("aggregate_type", aggregateType);
        command.Parameters.AddWithValue("aggregate_id", aggregateId);
        command.Parameters.AddWithValue("related_entity_type", relatedEntityId.HasValue ? "ReconciliationRun" : DBNull.Value);
        command.Parameters.AddWithValue("related_entity_id", (object?)relatedEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("payload_ref", payloadRef);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("actor_user_id", (object?)actorUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("actor_service_identity_id", (object?)actorServiceIdentityId ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)correlationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causation_id", (object?)causationId ?? DBNull.Value);
        command.Parameters.AddWithValue("routing_key", $"central-pms.reconciliation.{eventType}");
        command.Parameters.AddWithValue("summary", summary);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
