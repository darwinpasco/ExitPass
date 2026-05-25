using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Eventing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Integration tests for Central PMS operational RBAC enforcement.
/// </summary>
public sealed class CentralPmsRbacIntegrationTests
{
    private static readonly Guid OutboxEventId = Guid.Parse("adadadad-adad-adad-adad-adadadadad01");
    private static readonly Guid EventPublicationId = Guid.Parse("adadadad-adad-adad-adad-adadadadad02");
    private static readonly Guid AggregateId = Guid.Parse("adadadad-adad-adad-adad-adadadadad03");
    private static readonly Guid DeadLetterId = Guid.Parse("adadadad-adad-adad-adad-adadadadad04");
    private static readonly Guid CheckpointId = Guid.Parse("adadadad-adad-adad-adad-adadadadad05");

    [Fact]
    public async Task ProtectedEndpoint_WhenUnauthenticated_Returns401()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/internal/events/outbox/pending");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task ProtectedEndpoint_WhenMissingPermission_Returns403()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "reconciliation.view");

        using var response = await client.PostAsJsonAsync(
            "/v1/internal/events/outbox/dispatch-once",
            new DispatchReconciliationOutboxOnceRequest(1, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task ReconciliationViewer_CanAccessReadEndpoint()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "event.outbox.dispatch");

        using var response = await client.GetAsync("/v1/internal/events/outbox/pending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PendingReconciliationOutboxEventsResponse>();
        body!.Count.Should().Be(1);
    }

    [Fact]
    public async Task EventOutboxDispatcher_CanDispatch()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "event.outbox.dispatch");

        using var response = await client.PostAsJsonAsync(
            "/v1/internal/events/outbox/dispatch-once",
            new DispatchReconciliationOutboxOnceRequest(1, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeadLetterReplay_WhenMissingReplayPermission_Returns403()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "event.recovery.view");

        using var response = await client.PostAsJsonAsync(
            $"/v1/internal/events/dead-letters/{DeadLetterId}/replay",
            new RequestDeadLetterReplayRequest(null, null, "TEST"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConsumerCheckpointUpdate_WhenViewerOnly_Returns403()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "event.checkpoint.view");

        using var response = await client.PostAsJsonAsync(
            "/v1/internal/events/consumer-checkpoints/rbac-test/status",
            new UpdateConsumerCheckpointStatusRequest("PAUSED", Guid.NewGuid(), "TEST"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static CustomWebApplicationFactory CreateFactory()
    {
        return new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReconciliationOutboxDispatcherService>();
                services.AddSingleton<IReconciliationOutboxDispatcherService>(new FakeDispatcherService());
                services.RemoveAll<IEventRecoveryService>();
                services.AddSingleton<IEventRecoveryService>(new FakeEventRecoveryService());
            });
    }

    private sealed class FakeDispatcherService : IReconciliationOutboxDispatcherService
    {
        public Task<ReconciliationOutboxDispatchResult> DispatchOnceAsync(
            DispatchReconciliationOutboxOnceCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReconciliationOutboxDispatchResult(
                command.Limit,
                1,
                1,
                0,
                0,
                new[]
                {
                    new ReconciliationOutboxDispatchItemResult(
                        OutboxEventId,
                        EventPublicationId,
                        "ReconciliationRunEvaluated",
                        true,
                        "PUBLISHED",
                        null,
                        "rbac-test")
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
                    null,
                    0,
                    10,
                    Guid.NewGuid(),
                    null)
            });
    }

    private sealed class FakeEventRecoveryService : IEventRecoveryService
    {
        public Task<IReadOnlyList<DeadLetterRecord>> ListDeadLettersAsync(ListDeadLettersQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeadLetterRecord>>(new[] { DeadLetter("OPEN") });

        public Task<DeadLetterRecord> GetDeadLetterAsync(GetDeadLetterQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(DeadLetter("OPEN"));

        public Task<DeadLetterRecord> RequestDeadLetterReplayAsync(RequestDeadLetterReplayCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(DeadLetter("REPLAY_REQUESTED"));

        public Task<DeadLetterRecord> MarkDeadLetterReplayOutcomeAsync(MarkDeadLetterReplayOutcomeCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(DeadLetter(command.OutcomeStatus));

        public Task<IReadOnlyList<ConsumerCheckpointRecord>> ListConsumerCheckpointsAsync(ListConsumerCheckpointsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConsumerCheckpointRecord>>(new[] { Checkpoint("ACTIVE") });

        public Task<ConsumerCheckpointRecord> GetConsumerCheckpointAsync(GetConsumerCheckpointQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Checkpoint("ACTIVE"));

        public Task<ConsumerCheckpointRecord> UpdateConsumerCheckpointStatusAsync(UpdateConsumerCheckpointStatusCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Checkpoint(command.CheckpointStatus));
    }

    private static DeadLetterRecord DeadLetter(string status) =>
        new(
            DeadLetterId,
            null,
            null,
            "rbac-test",
            "CONSUMER_FAILURE",
            status,
            "TEST",
            null,
            null,
            DateTimeOffset.UtcNow,
            status == "REPLAY_REQUESTED" ? DateTimeOffset.UtcNow : null,
            null,
            null,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static ConsumerCheckpointRecord Checkpoint(string status) =>
        new(
            CheckpointId,
            "rbac-test",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            status,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);
}
