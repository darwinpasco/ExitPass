using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Infrastructure.Eventing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for RabbitMQ-backed reconciliation outbox publisher support.
/// </summary>
public sealed class ReconciliationRabbitMqOutboxPublisherTests
{
    /// <summary>
    /// Verifies RabbitMQ reconciliation outbox options require explicit enablement and host configuration.
    /// </summary>
    [Fact]
    public void Options_WhenDisabled_DoNotConfigureRabbitMqPublisher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:Host"] = "localhost",
                ["Messaging:RabbitMq:ReconciliationOutbox:Enabled"] = "false"
            })
            .Build();

        var options = RabbitMqReconciliationOutboxPublisherOptions.FromConfiguration(configuration);

        options.Enabled.Should().BeFalse();
        options.IsConfigured.Should().BeFalse();
        options.Host.Should().Be("localhost");
    }

    /// <summary>
    /// Verifies RabbitMQ reconciliation outbox options can inherit common broker settings.
    /// </summary>
    [Fact]
    public void Options_WhenEnabled_UseCommonRabbitMqSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:Host"] = "rabbitmq.local",
                ["Messaging:RabbitMq:Port"] = "5673",
                ["Messaging:RabbitMq:Username"] = "central-pms",
                ["Messaging:RabbitMq:Password"] = "not-logged",
                ["Messaging:RabbitMq:ExchangeName"] = "exitpass.central-pms.events",
                ["Messaging:RabbitMq:ReconciliationOutbox:Enabled"] = "true",
                ["Messaging:RabbitMq:ReconciliationOutbox:RoutingKeyPrefix"] = "central-pms.reconciliation"
            })
            .Build();

        var options = RabbitMqReconciliationOutboxPublisherOptions.FromConfiguration(configuration);

        options.IsConfigured.Should().BeTrue();
        options.Host.Should().Be("rabbitmq.local");
        options.Port.Should().Be(5673);
        options.Username.Should().Be("central-pms");
        options.ExchangeName.Should().Be("exitpass.central-pms.events");
        options.RoutingKeyPrefix.Should().Be("central-pms.reconciliation");
    }

    /// <summary>
    /// Verifies the RabbitMQ message envelope preserves durable outbox correlation metadata.
    /// </summary>
    [Fact]
    public void ToMessage_BuildsDeterministicEnvelopeFromOutboxRecord()
    {
        var outboxEvent = OutboxEvent();

        var message = RabbitMqReconciliationOutboxEventPublisher.ToMessage(outboxEvent);

        message.EventId.Should().Be(outboxEvent.OutboxEventId);
        message.EventType.Should().Be(outboxEvent.EventType);
        message.AggregateId.Should().Be(outboxEvent.AggregateId);
        message.CorrelationId.Should().Be(outboxEvent.CorrelationId);
        message.CausationId.Should().Be(outboxEvent.CausationId);
        message.OccurredAtUtc.Should().Be(outboxEvent.CreatedAt);
        message.Payload.PayloadRef.Should().Be(outboxEvent.PayloadRef);
        message.Payload.PayloadContentType.Should().Be("application/json");
    }

    /// <summary>
    /// Verifies disabled RabbitMQ outbox configuration keeps the in-process test publisher.
    /// </summary>
    [Fact]
    public void AddCentralPmsReconciliationOutboxPublisher_WhenDisabled_UsesInProcessPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentralPmsReconciliationOutboxPublisher(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReconciliationOutboxEventPublisher>()
            .Should().BeOfType<InProcessReconciliationOutboxEventPublisher>();
    }

    /// <summary>
    /// Verifies enabled RabbitMQ outbox configuration wires the RabbitMQ publisher.
    /// </summary>
    [Fact]
    public void AddCentralPmsReconciliationOutboxPublisher_WhenConfigured_UsesRabbitMqPublisher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:RabbitMq:ReconciliationOutbox:Enabled"] = "true",
                ["Messaging:RabbitMq:Host"] = "localhost"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentralPmsReconciliationOutboxPublisher(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReconciliationOutboxEventPublisher>()
            .Should().BeOfType<RabbitMqReconciliationOutboxEventPublisher>();
    }

    /// <summary>
    /// Verifies a configured-but-unreachable RabbitMQ publisher returns a deterministic failed outcome.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenRabbitMqUnavailable_ReturnsFailedOutcome()
    {
        var options = new RabbitMqReconciliationOutboxPublisherOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = 1,
            Username = "guest",
            Password = "guest",
            VirtualHost = "/",
            PublishConfirmTimeout = TimeSpan.FromMilliseconds(100)
        };
        var publisher = new RabbitMqReconciliationOutboxEventPublisher(
            options,
            NullLogger<RabbitMqReconciliationOutboxEventPublisher>.Instance);

        var outcome = await publisher.PublishAsync(OutboxEvent(), CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReasonCode.Should().Be("RABBITMQ_PUBLISH_FAILED");
        outcome.FailureDetailRef.Should().NotContain(options.Password);
    }

    private static ReconciliationOutboxEventRecord OutboxEvent() =>
        new(
            OutboxEventId: Guid.Parse("abababab-abab-abab-abab-ababababab01"),
            DomainEventId: Guid.Parse("abababab-abab-abab-abab-ababababab02"),
            EventPublicationId: Guid.Parse("abababab-abab-abab-abab-ababababab03"),
            PublicationAttemptNumber: 1,
            EventType: "ReconciliationItemEvaluated",
            EventVersion: 1,
            AggregateType: "ReconciliationItem",
            AggregateId: Guid.Parse("abababab-abab-abab-abab-ababababab04"),
            RoutingKey: "central-pms.reconciliation.ReconciliationItemEvaluated",
            ExchangeName: "exitpass.central-pms",
            PayloadRef: "central-pms://reconciliation-events/abababab",
            PayloadHash: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PayloadContentType: "application/json",
            CorrelationId: Guid.Parse("abababab-abab-abab-abab-ababababab05"),
            CausationId: Guid.Parse("abababab-abab-abab-abab-ababababab06"),
            RetryCount: 0,
            MaxRetryCount: 10,
            CreatedAt: DateTimeOffset.Parse("2026-05-24T10:00:00Z"));
}
