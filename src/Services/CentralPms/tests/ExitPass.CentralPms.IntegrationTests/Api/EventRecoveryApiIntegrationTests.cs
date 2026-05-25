using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Eventing;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// API integration tests for internal event recovery endpoints.
/// </summary>
public sealed class EventRecoveryApiIntegrationTests
{
    private static readonly Guid DeadLetterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc01");
    private static readonly Guid CheckpointId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc02");

    /// <summary>
    /// Verifies dead-letter list endpoint returns records.
    /// </summary>
    [Fact]
    public async Task DeadLetters_ReturnsRecords()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/internal/events/dead-letters?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DeadLetterRecordsResponse>();
        body.Should().NotBeNull();
        body!.Items.Single().DeadLetterRecordId.Should().Be(DeadLetterId);
    }

    /// <summary>
    /// Verifies unknown dead-letter id returns a deterministic error envelope.
    /// </summary>
    [Fact]
    public async Task DeadLetter_WhenUnknown_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/internal/events/dead-letters/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("DEAD_LETTER_RECORD_NOT_FOUND");
    }

    /// <summary>
    /// Verifies replay endpoint returns requested status.
    /// </summary>
    [Fact]
    public async Task DeadLetterReplay_ReturnsReplayRequested()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/internal/events/dead-letters/{DeadLetterId}/replay",
            new RequestDeadLetterReplayRequest(null, null, "API_TEST"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DeadLetterReplayResponse>();
        body.Should().NotBeNull();
        body!.DeadLetterStatus.Should().Be("REPLAY_REQUESTED");
    }

    /// <summary>
    /// Verifies consumer checkpoint list endpoint returns records.
    /// </summary>
    [Fact]
    public async Task ConsumerCheckpoints_ReturnsRecords()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/internal/events/consumer-checkpoints?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ConsumerCheckpointsResponse>();
        body.Should().NotBeNull();
        body!.Items.Single().ConsumerCheckpointId.Should().Be(CheckpointId);
    }

    /// <summary>
    /// Verifies event recovery endpoints carry policy metadata placeholders.
    /// </summary>
    [Fact]
    public void EventRecoveryEndpoints_HavePolicyMetadata()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/v1/internal/events/", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == "/v1/internal/events/dead-letters");
        endpoints.Where(endpoint => endpoint.RoutePattern.RawText?.Contains("dead-letters") == true)
            .Should()
            .OnlyContain(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>() != null);
        endpoints.Where(endpoint => endpoint.RoutePattern.RawText?.Contains("consumer-checkpoints") == true)
            .Should()
            .OnlyContain(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>() != null);
    }

    private static CustomWebApplicationFactory CreateFactory()
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IEventRecoveryService>();
                services.AddSingleton<IEventRecoveryService>(new FakeEventRecoveryService());
            });
    }

    private sealed class FakeEventRecoveryService : IEventRecoveryService
    {
        public Task<IReadOnlyList<DeadLetterRecord>> ListDeadLettersAsync(
            ListDeadLettersQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeadLetterRecord>>(new[] { DeadLetter("OPEN") });

        public Task<DeadLetterRecord> GetDeadLetterAsync(
            GetDeadLetterQuery query,
            CancellationToken cancellationToken) =>
            query.DeadLetterRecordId == DeadLetterId
                ? Task.FromResult(DeadLetter("OPEN"))
                : Task.FromException<DeadLetterRecord>(new InvalidOperationException("DEAD_LETTER_RECORD_NOT_FOUND"));

        public Task<DeadLetterRecord> RequestDeadLetterReplayAsync(
            RequestDeadLetterReplayCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeadLetter("REPLAY_REQUESTED"));

        public Task<DeadLetterRecord> MarkDeadLetterReplayOutcomeAsync(
            MarkDeadLetterReplayOutcomeCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeadLetter(command.OutcomeStatus));

        public Task<IReadOnlyList<ConsumerCheckpointRecord>> ListConsumerCheckpointsAsync(
            ListConsumerCheckpointsQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConsumerCheckpointRecord>>(new[] { Checkpoint("ACTIVE") });

        public Task<ConsumerCheckpointRecord> GetConsumerCheckpointAsync(
            GetConsumerCheckpointQuery query,
            CancellationToken cancellationToken) =>
            query.ConsumerName == "event-recovery-test"
                ? Task.FromResult(Checkpoint("ACTIVE"))
                : Task.FromException<ConsumerCheckpointRecord>(new InvalidOperationException("CONSUMER_CHECKPOINT_NOT_FOUND"));

        public Task<ConsumerCheckpointRecord> UpdateConsumerCheckpointStatusAsync(
            UpdateConsumerCheckpointStatusCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Checkpoint(command.CheckpointStatus));
    }

    private static DeadLetterRecord DeadLetter(string status) =>
        new(
            DeadLetterId,
            OutboxEventId: null,
            EventPublicationId: null,
            ConsumerName: "event-recovery-test",
            DeadLetterType: "CONSUMER_FAILURE",
            DeadLetterStatus: status,
            FailureReasonCode: "TEST",
            FailureDetailRef: null,
            PayloadHash: null,
            DeadLetteredAt: DateTimeOffset.UtcNow,
            ReplayRequestedAt: status == "REPLAY_REQUESTED" ? DateTimeOffset.UtcNow : null,
            ResolvedAt: status is "REPLAYED" or "RESOLVED" or "REJECTED" ? DateTimeOffset.UtcNow : null,
            ResolutionReasonCode: null,
            CorrelationId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    private static ConsumerCheckpointRecord Checkpoint(string status) =>
        new(
            CheckpointId,
            ConsumerName: "event-recovery-test",
            ConsumerGroup: null,
            SubscriptionName: null,
            EventType: null,
            AggregateType: null,
            LastOutboxEventId: null,
            LastDomainEventId: null,
            LastBrokerOffset: null,
            CheckpointStatus: status,
            ProcessedCount: 0,
            FailureCount: 0,
            LastProcessedAt: null,
            LastFailedAt: null,
            FailureReasonCode: null,
            LockedAt: null,
            LockedByServiceIdentityId: null,
            UpdatedByServiceIdentityId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            CorrelationId: null);
}
