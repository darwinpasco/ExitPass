using ExitPass.CentralPms.Application.Eventing;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for reconciliation outbox dispatch coordination.
/// </summary>
public sealed class ReconciliationOutboxDispatcherServiceTests
{
    private static readonly Guid OutboxEventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01");
    private static readonly Guid EventPublicationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd02");

    /// <summary>
    /// Verifies successful publication is durably marked as published.
    /// </summary>
    [Fact]
    public async Task DispatchOnce_WhenPublisherSucceeds_MarksEventPublished()
    {
        var repository = Substitute.For<IReconciliationOutboxDispatcherRepository>();
        var publisher = Substitute.For<IReconciliationOutboxEventPublisher>();
        publisher.BrokerType.Returns("IN_PROCESS");
        var claimed = EventRecord();
        repository.ClaimPendingAsync(Arg.Any<DispatchReconciliationOutboxOnceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new[] { claimed });
        publisher.PublishAsync(claimed, Arg.Any<CancellationToken>())
            .Returns(new ReconciliationOutboxPublishOutcome(true, "broker-message-1", null, null));

        var service = new ReconciliationOutboxDispatcherService(repository, publisher);

        var result = await service.DispatchOnceAsync(new DispatchReconciliationOutboxOnceCommand(10, null), CancellationToken.None);

        result.ClaimedCount.Should().Be(1);
        result.PublishedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.Items.Single().PublicationStatus.Should().Be("PUBLISHED");
        result.Items.Single().BrokerMessageId.Should().Be("broker-message-1");
        await repository.Received(1).MarkPublishedAsync(
            claimed,
            Arg.Is<ReconciliationOutboxPublishOutcome>(outcome => outcome.Succeeded),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies failed publication is handed to durable retry/dead-letter state handling.
    /// </summary>
    [Fact]
    public async Task DispatchOnce_WhenPublisherFails_MarksEventFailed()
    {
        var repository = Substitute.For<IReconciliationOutboxDispatcherRepository>();
        var publisher = Substitute.For<IReconciliationOutboxEventPublisher>();
        publisher.BrokerType.Returns("IN_PROCESS");
        var claimed = EventRecord();
        repository.ClaimPendingAsync(Arg.Any<DispatchReconciliationOutboxOnceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new[] { claimed });
        publisher.PublishAsync(claimed, Arg.Any<CancellationToken>())
            .Returns(new ReconciliationOutboxPublishOutcome(false, null, "BROKER_UNAVAILABLE", "test://failure"));
        repository.MarkFailedAsync(claimed, Arg.Any<ReconciliationOutboxPublishOutcome>(), Arg.Any<CancellationToken>())
            .Returns(new ReconciliationOutboxDispatchItemResult(
                claimed.OutboxEventId,
                claimed.EventPublicationId,
                claimed.EventType,
                false,
                "RETRY_PENDING",
                "BROKER_UNAVAILABLE",
                null));

        var service = new ReconciliationOutboxDispatcherService(repository, publisher);

        var result = await service.DispatchOnceAsync(new DispatchReconciliationOutboxOnceCommand(10, null), CancellationToken.None);

        result.PublishedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Items.Single().PublicationStatus.Should().Be("RETRY_PENDING");
        await repository.Received(1).MarkFailedAsync(
            claimed,
            Arg.Is<ReconciliationOutboxPublishOutcome>(outcome => outcome.FailureReasonCode == "BROKER_UNAVAILABLE"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies no claimed rows means no in-memory publication is attempted.
    /// </summary>
    [Fact]
    public async Task DispatchOnce_WhenNoRowsClaimed_DoesNotPublish()
    {
        var repository = Substitute.For<IReconciliationOutboxDispatcherRepository>();
        var publisher = Substitute.For<IReconciliationOutboxEventPublisher>();
        publisher.BrokerType.Returns("IN_PROCESS");
        repository.ClaimPendingAsync(Arg.Any<DispatchReconciliationOutboxOnceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationOutboxEventRecord>());

        var service = new ReconciliationOutboxDispatcherService(repository, publisher);

        var result = await service.DispatchOnceAsync(new DispatchReconciliationOutboxOnceCommand(10, null), CancellationToken.None);

        result.ClaimedCount.Should().Be(0);
        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    /// <summary>
    /// Verifies default and maximum limits are deterministic.
    /// </summary>
    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(150, 100)]
    [InlineData(50, 50)]
    public async Task DispatchOnce_NormalizesLimit(int requestedLimit, int expectedLimit)
    {
        var repository = Substitute.For<IReconciliationOutboxDispatcherRepository>();
        var publisher = Substitute.For<IReconciliationOutboxEventPublisher>();
        publisher.BrokerType.Returns("RABBITMQ");
        repository.ClaimPendingAsync(Arg.Any<DispatchReconciliationOutboxOnceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationOutboxEventRecord>());

        var service = new ReconciliationOutboxDispatcherService(repository, publisher);

        await service.DispatchOnceAsync(new DispatchReconciliationOutboxOnceCommand(requestedLimit, null), CancellationToken.None);

        await repository.Received(1).ClaimPendingAsync(
            Arg.Is<DispatchReconciliationOutboxOnceCommand>(command => command.Limit == expectedLimit && command.BrokerType == "RABBITMQ"),
            Arg.Any<CancellationToken>());
    }

    private static ReconciliationOutboxEventRecord EventRecord() =>
        new(
            OutboxEventId: OutboxEventId,
            DomainEventId: null,
            EventPublicationId: EventPublicationId,
            PublicationAttemptNumber: 1,
            EventType: "ReconciliationRunEvaluated",
            EventVersion: 1,
            AggregateType: "ReconciliationRun",
            AggregateId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd03"),
            RoutingKey: "central-pms.reconciliation.ReconciliationRunEvaluated",
            ExchangeName: "exitpass.central-pms",
            PayloadRef: "central-pms://reconciliation-events/test",
            PayloadHash: null,
            PayloadContentType: "application/json",
            CorrelationId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd04"),
            CausationId: null,
            RetryCount: 0,
            MaxRetryCount: 10,
            CreatedAt: DateTimeOffset.Parse("2026-05-24T10:00:00Z"));
}
