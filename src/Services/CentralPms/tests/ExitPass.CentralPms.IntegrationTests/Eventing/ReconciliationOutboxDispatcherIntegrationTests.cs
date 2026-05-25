using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Eventing;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Eventing;

/// <summary>
/// Integration tests for durable reconciliation outbox dispatch state transitions.
/// </summary>
public sealed class ReconciliationOutboxDispatcherIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies a pending reconciliation outbox event is claimed, published, and recorded in event_publications.
    /// </summary>
    [Fact]
    public async Task ReconciliationOutbox_DispatchSuccess_MarksPublishedAndRecordsPublicationEvidence()
    {
        var outboxEventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var publisherServiceIdentityId = await ResolvePublisherServiceIdentityIdAsync();

        await InsertOutboxEventAsync(outboxEventId, aggregateId, correlationId);

        try
        {
            var service = CreateService(new InProcessReconciliationOutboxEventPublisher());

            var result = await service.DispatchOnceAsync(
                new DispatchReconciliationOutboxOnceCommand(10, publisherServiceIdentityId),
                CancellationToken.None);
            var second = await service.DispatchOnceAsync(
                new DispatchReconciliationOutboxOnceCommand(10, publisherServiceIdentityId),
                CancellationToken.None);

            result.ClaimedCount.Should().Be(1);
            result.PublishedCount.Should().Be(1);
            result.FailedCount.Should().Be(0);
            second.ClaimedCount.Should().Be(0);

            var state = await ReadOutboxStateAsync(outboxEventId, correlationId);
            state.PublicationStatus.Should().Be("PUBLISHED");
            state.PublishedPublicationCount.Should().Be(1);
            state.FailedPublicationCount.Should().Be(0);
            state.BrokerType.Should().Be("IN_PROCESS");
        }
        finally
        {
            await CleanupAsync(outboxEventId, correlationId);
        }
    }

    /// <summary>
    /// Verifies failed publication records deterministic retry-pending evidence.
    /// </summary>
    [Fact]
    public async Task ReconciliationOutbox_DispatchFailure_MarksRetryPendingAndRecordsFailedPublication()
    {
        var outboxEventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var publisherServiceIdentityId = await ResolvePublisherServiceIdentityIdAsync();

        await InsertOutboxEventAsync(outboxEventId, aggregateId, correlationId);

        try
        {
            var service = CreateService(new FailingPublisher());

            var result = await service.DispatchOnceAsync(
                new DispatchReconciliationOutboxOnceCommand(10, publisherServiceIdentityId),
                CancellationToken.None);

            result.ClaimedCount.Should().Be(1);
            result.PublishedCount.Should().Be(0);
            result.FailedCount.Should().Be(1);
            result.Items.Single().PublicationStatus.Should().Be("RETRY_PENDING");

            var state = await ReadOutboxStateAsync(outboxEventId, correlationId);
            state.PublicationStatus.Should().Be("RETRY_PENDING");
            state.RetryCount.Should().Be(1);
            state.PublishedPublicationCount.Should().Be(0);
            state.FailedPublicationCount.Should().Be(1);
        }
        finally
        {
            await CleanupAsync(outboxEventId, correlationId);
        }
    }

    private static IReconciliationOutboxDispatcherService CreateService(IReconciliationOutboxEventPublisher publisher)
    {
        var repository = new ReconciliationOutboxDispatcherRepository(
            ConnectionString,
            NullLogger<ReconciliationOutboxDispatcherRepository>.Instance);
        return new ReconciliationOutboxDispatcherService(repository, publisher);
    }

    private static async Task InsertOutboxEventAsync(Guid outboxEventId, Guid aggregateId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events.outbox_events (
                outbox_event_id,
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
                created_at,
                updated_at
            )
            VALUES (
                @outbox_event_id,
                'reconciliation',
                'reconciliation_items',
                'ReconciliationItemEvaluated',
                1,
                'ReconciliationItem',
                @aggregate_id,
                'central-pms.reconciliation.ReconciliationItemEvaluated',
                'exitpass.central-pms',
                'central-pms://reconciliation-events/integration-test',
                'application/json',
                'PENDING',
                now(),
                0,
                10,
                @correlation_id,
                now(),
                now()
            );
            """;
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("aggregate_id", aggregateId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ResolvePublisherServiceIdentityIdAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT service_identity_id
            FROM identity.service_identities
            WHERE identity_status::text = 'ACTIVE'
              AND service_identity_code IN ('central-pms', 'CENTRAL_PMS_API')
            ORDER BY CASE service_identity_code WHEN 'central-pms' THEN 0 ELSE 1 END
            LIMIT 1;
            """;

        var value = await command.ExecuteScalarAsync();
        value.Should().BeOfType<Guid>();
        return (Guid)value!;
    }

    private static async Task<OutboxState> ReadOutboxStateAsync(Guid outboxEventId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                oe.publication_status::text AS publication_status,
                oe.retry_count,
                (
                    SELECT COUNT(*)
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                      AND ep.publication_status = 'PUBLISHED'
                ) AS published_publication_count,
                (
                    SELECT COUNT(*)
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                      AND ep.publication_status = 'FAILED'
                ) AS failed_publication_count,
                (
                    SELECT ep.broker_type::text
                    FROM events.event_publications ep
                    WHERE ep.outbox_event_id = oe.outbox_event_id
                    ORDER BY ep.publication_attempt_number DESC
                    LIMIT 1
                ) AS broker_type
            FROM events.outbox_events oe
            WHERE oe.outbox_event_id = @outbox_event_id
              AND oe.correlation_id = @correlation_id;
            """;
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        return new OutboxState(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task CleanupAsync(Guid outboxEventId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM events.dead_letter_records WHERE outbox_event_id = @outbox_event_id OR correlation_id = @correlation_id;
            DELETE FROM events.event_publications WHERE outbox_event_id = @outbox_event_id OR correlation_id = @correlation_id;
            DELETE FROM events.outbox_events WHERE outbox_event_id = @outbox_event_id OR correlation_id = @correlation_id;
            """;
        command.Parameters.AddWithValue("outbox_event_id", outboxEventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await command.ExecuteNonQueryAsync();
    }

    private sealed class FailingPublisher : IReconciliationOutboxEventPublisher
    {
        public Task<ReconciliationOutboxPublishOutcome> PublishAsync(
            ReconciliationOutboxEventRecord outboxEvent,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReconciliationOutboxPublishOutcome(
                Succeeded: false,
                BrokerMessageId: null,
                FailureReasonCode: "BROKER_UNAVAILABLE",
                FailureDetailRef: "integration-test://broker-unavailable"));
    }

    private sealed record OutboxState(
        string PublicationStatus,
        int RetryCount,
        long PublishedPublicationCount,
        long FailedPublicationCount,
        string? BrokerType);
}
