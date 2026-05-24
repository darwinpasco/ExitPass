using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Eventing;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Eventing;

/// <summary>
/// Integration tests for durable Central PMS event evidence persistence.
/// </summary>
public sealed class DurableIntegrationEventPublisherTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies that integration events are persisted to the durable event, outbox, and audit tables even when broker publishing is disabled.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenRabbitMqIsDisabled_PersistsDurableEventOutboxAndAuditRows()
    {
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddCentralPmsEventPublishing(
            new ConfigurationBuilder().Build(),
            ConnectionString);

        await using var serviceProvider = services.BuildServiceProvider();
        var publisher = serviceProvider.GetRequiredService<IIntegrationEventPublisher>();

        try
        {
            await publisher.PublishAsync(
                new IntegrationEventEnvelope
                {
                    EventId = eventId,
                    EventType = IntegrationEventTypes.PaymentAttemptConfirmed,
                    OccurredAtUtc = DateTimeOffset.Parse("2026-05-24T10:00:00Z"),
                    CorrelationId = correlationId,
                    AggregateId = aggregateId.ToString(),
                    AggregateType = "PaymentAttempt",
                    Payload = new PaymentAttemptConfirmedPayload
                    {
                        PaymentAttemptId = aggregateId,
                        AttemptStatus = "CONFIRMED",
                        ProviderReference = "pay_test_durable_event"
                    }
                },
                CancellationToken.None);

            var counts = await ReadEvidenceCountsAsync(eventId, correlationId);

            counts.DomainEventCount.Should().Be(1);
            counts.OutboxEventCount.Should().Be(1);
            counts.AuditEventCount.Should().Be(1);
        }
        finally
        {
            await CleanupAsync(eventId, correlationId);
        }
    }

    private static async Task<EvidenceCounts> ReadEvidenceCountsAsync(Guid eventId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM events.domain_events WHERE domain_event_id = @event_id AND correlation_id = @correlation_id) AS domain_event_count,
                (SELECT COUNT(*) FROM events.outbox_events WHERE domain_event_id = @event_id AND correlation_id = @correlation_id) AS outbox_event_count,
                (SELECT COUNT(*) FROM audit.audit_events WHERE correlation_id = @correlation_id AND event_type = @event_type) AS audit_event_count;
            """;
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("event_type", IntegrationEventTypes.PaymentAttemptConfirmed);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new EvidenceCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2));
    }

    private static async Task CleanupAsync(Guid eventId, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM events.outbox_events WHERE domain_event_id = @event_id OR correlation_id = @correlation_id;
            DELETE FROM events.domain_events WHERE domain_event_id = @event_id OR correlation_id = @correlation_id;
            DELETE FROM audit.audit_events WHERE correlation_id = @correlation_id;
            """;
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await command.ExecuteNonQueryAsync();
    }

    private sealed record EvidenceCounts(
        long DomainEventCount,
        long OutboxEventCount,
        long AuditEventCount);
}
