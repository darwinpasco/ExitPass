using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Contracts.Eventing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Central PMS internal reconciliation outbox dispatcher API surface.
/// </summary>
public sealed class ReconciliationOutboxDispatcherApiIntegrationTests
{
    private static readonly Guid OutboxEventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01");
    private static readonly Guid EventPublicationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee02");
    private static readonly Guid AggregateId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee03");
    private static readonly Guid CorrelationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee04");

    /// <summary>
    /// Verifies dispatch-once returns durable dispatch results from the service boundary.
    /// </summary>
    [Fact]
    public async Task DispatchOnce_ReturnsDispatchSummary()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/internal/events/outbox/dispatch-once",
            new DispatchReconciliationOutboxOnceRequest(5, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DispatchReconciliationOutboxOnceResponse>();
        body.Should().NotBeNull();
        body!.ClaimedCount.Should().Be(1);
        body.PublishedCount.Should().Be(1);
        body.Items.Single().PublicationStatus.Should().Be("PUBLISHED");
    }

    /// <summary>
    /// Verifies pending reconciliation outbox events can be listed.
    /// </summary>
    [Fact]
    public async Task Pending_ReturnsPendingEvents()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/internal/events/outbox/pending?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PendingReconciliationOutboxEventsResponse>();
        body.Should().NotBeNull();
        body!.Count.Should().Be(1);
        body.Items.Single().EventType.Should().Be("ReconciliationRunEvaluated");
    }

    private static CustomWebApplicationFactory CreateFactory()
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReconciliationOutboxDispatcherService>();
                services.AddSingleton<IReconciliationOutboxDispatcherService>(new FakeDispatcherService());
            });
    }

    private sealed class FakeDispatcherService : IReconciliationOutboxDispatcherService
    {
        public Task<ReconciliationOutboxDispatchResult> DispatchOnceAsync(
            DispatchReconciliationOutboxOnceCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReconciliationOutboxDispatchResult(
                command.Limit,
                ClaimedCount: 1,
                PublishedCount: 1,
                FailedCount: 0,
                DeadLetteredCount: 0,
                new[]
                {
                    new ReconciliationOutboxDispatchItemResult(
                        OutboxEventId: OutboxEventId,
                        EventPublicationId: EventPublicationId,
                        EventType: "ReconciliationRunEvaluated",
                        Succeeded: true,
                        PublicationStatus: "PUBLISHED",
                        FailureReasonCode: null,
                        BrokerMessageId: "in-process-test")
                }));

        public Task<IReadOnlyList<ReconciliationOutboxPendingRecord>> ListPendingAsync(
            ListPendingReconciliationOutboxQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReconciliationOutboxPendingRecord>>(new[]
            {
                new ReconciliationOutboxPendingRecord(
                    OutboxEventId,
                    "ReconciliationRunEvaluated",
                    "ReconciliationRun",
                    AggregateId,
                    "PENDING",
                    DateTimeOffset.UtcNow,
                    NextRetryAt: null,
                    RetryCount: 0,
                    MaxRetryCount: 10,
                    CorrelationId,
                    CausationId: null)
            });
    }
}
