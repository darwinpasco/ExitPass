using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Auditing;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Infrastructure.Auditing;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.VendorPmsAdapter.Contracts.Projection;
using ExitPass.VendorPmsAdapter.Contracts.Routing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Auditing;

public sealed class SiteVendorAdapterProjectionAuditTests
{
    [Fact]
    public async Task Sync_PublishesSiteScopedAuditBeforeAtomicProjectionPersistence()
    {
        var fixture = new Fixture();

        await fixture.Service.SyncAsync(fixture.Command, CancellationToken.None);

        await fixture.Audit.Received(1).AppendAsync(
            Arg.Is<ApplicationAuditEvent>(item =>
                item.SiteId == fixture.SiteId &&
                item.CorrelationId == fixture.CorrelationId &&
                item.EventType == "VENDOR_SESSION_PROJECTION_BATCH_RECEIVED"),
            Arg.Any<CancellationToken>());
        await fixture.Repository.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyList<VendorSessionProjection>>(items => items.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_WhenAuditPublicationFails_DoesNotPersistProjectionBatch()
    {
        var fixture = new Fixture();
        fixture.Audit.AppendAsync(Arg.Any<ApplicationAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AuditEventPublishException("AUDIT_EVENT_SERVICE_UNAVAILABLE")));

        await Assert.ThrowsAsync<AuditEventPublishException>(() =>
            fixture.Service.SyncAsync(fixture.Command, CancellationToken.None));

        await fixture.Repository.DidNotReceive().UpsertBatchAsync(
            Arg.Any<IReadOnlyList<VendorSessionProjection>>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public readonly Guid SiteId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private readonly Guid siteGroupId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        private readonly Guid vendorSystemId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        private readonly Guid adapterIdentityId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        public readonly Guid CorrelationId = Guid.Parse("10000000-0000-0000-0000-000000000005");
        public readonly IVendorSessionProjectionRepository Repository = Substitute.For<IVendorSessionProjectionRepository>();
        public readonly IAuditEventPublisher Audit = Substitute.For<IAuditEventPublisher>();
        public readonly SiteVendorAdapterProjectionSyncService Service;

        public Fixture()
        {
            var route = new SiteVendorAdapterRoute(SiteId, siteGroupId, vendorSystemId, adapterIdentityId,
                new Uri("http://adapter-a"), "adapter-a-key", "IntegrationTest",
                DateTimeOffset.Parse("2026-08-23T00:00:00Z"), null);
            var routes = Substitute.For<ISiteVendorAdapterRouteRegistry>();
            routes.ResolveAsync(SiteId, siteGroupId, vendorSystemId, Arg.Any<CancellationToken>()).Returns(route);
            var credentials = Substitute.For<ISiteAdapterCredentialResolver>();
            credentials.Resolve("adapter-a-key").Returns("mounted-test-key");
            var clock = Substitute.For<ISystemClock>();
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-08-23T00:01:00Z"));
            var response = new VendorPassagewaySyncResponse(true, 1, 1, 1, 0,
                [new VendorPassagewayRecordDto("record-a", "ticket-a", "PLATEA", "LOT-A", "Site A",
                    "entry-a", "Entry A", "lane-a", "Lane A", "IN", DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
                    null, "ALLOW", "SUCCESS", "HIKCENTRAL_V3_1", "hash-a",
                    DateTimeOffset.Parse("2026-08-23T00:00:00Z"))], null, false, CorrelationId,
                new VendorAdapterResponseContext(SiteId, siteGroupId, vendorSystemId, adapterIdentityId,
                    "LOT-A", "IntegrationTest"));
            var http = new HttpClient(new JsonHandler(response));
            Service = new SiteVendorAdapterProjectionSyncService(http, routes, credentials, Repository, clock,
                Substitute.For<ILogger<SiteVendorAdapterProjectionSyncService>>(),
                Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2"), true, Audit);
        }

        public SyncVendorSessionProjectionsCommand Command => new(vendorSystemId, SiteId, siteGroupId, "LOT-A",
            DateTimeOffset.Parse("2026-08-22T23:00:00Z"), DateTimeOffset.Parse("2026-08-23T00:01:00Z"),
            100, 1, CorrelationId);
    }

    private sealed class JsonHandler(VendorPassagewaySyncResponse response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
    }
}
