using System.Net;
using System.Text.Json;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.CentralPms.Infrastructure.VendorParking.Routing;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Routing;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class SiteVendorAdapterRoutingTests
{
    private static readonly Guid Group = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteB = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid VendorA = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid VendorB = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid AdapterA = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid AdapterB = Guid.Parse("40000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task SiteAAndSiteB_CallOnlyTheirSelectedAdapters()
    {
        var transport = new RecordingHandler();
        var client = CreateClient(transport);
        await client.ResolveSessionAsync(Request(SiteA, VendorA), default);
        await client.ResolveSessionAsync(Request(SiteB, VendorB), default);
        Assert.Equal(new[] { "adapter-a.internal", "adapter-b.internal" }, transport.Hosts);
        Assert.Single(transport.ServiceIdentities.Distinct());
    }

    [Fact]
    public async Task CrossSiteAdapterIdentity_FailsBeforeTransport()
    {
        var transport = new RecordingHandler();
        var client = CreateClient(transport);
        var request = Request(SiteA, VendorA) with
        {
            Context = new(SiteA, Group, VendorA, AdapterB)
        };
        var error = await Assert.ThrowsAsync<SiteVendorAdapterRoutingException>(() =>
            client.ResolveSessionAsync(request, default));
        Assert.Equal("SITE_ADAPTER_IMMUTABLE_ROUTE_MISMATCH", error.ErrorCode);
        Assert.Empty(transport.Hosts);
    }

    [Fact]
    public async Task MissingMapping_FailsClosedBeforeTransport()
    {
        var transport = new RecordingHandler();
        var client = new SiteVendorAdapterHttpClient(new HttpClient(transport), new MissingRegistry(),
            new CredentialResolver(), Guid.NewGuid(), true);
        var error = await Assert.ThrowsAsync<SiteVendorAdapterRoutingException>(() =>
            client.ResolveSessionAsync(Request(SiteA, VendorA), default));
        Assert.Equal("SITE_ADAPTER_MAPPING_NOT_FOUND", error.ErrorCode);
        Assert.Empty(transport.Hosts);
    }

    [Fact]
    public async Task WrongVendorForSite_FailsBeforeTransport()
    {
        var transport = new RecordingHandler();
        var client = CreateClient(transport);
        var error = await Assert.ThrowsAsync<SiteVendorAdapterRoutingException>(() =>
            client.ResolveSessionAsync(Request(SiteA, VendorB), default));
        Assert.Equal("SITE_ADAPTER_MAPPING_NOT_FOUND", error.ErrorCode);
        Assert.Empty(transport.Hosts);
    }

    [Fact]
    public async Task ResponseFromDifferentAdapter_FailsClosed()
    {
        var transport = new RecordingHandler { OverrideAdapterIdentity = AdapterB };
        var client = CreateClient(transport);
        var error = await Assert.ThrowsAsync<SiteVendorAdapterRoutingException>(() =>
            client.ResolveSessionAsync(Request(SiteA, VendorA), default));
        Assert.Equal("SITE_ADAPTER_RESPONSE_BINDING_MISMATCH", error.ErrorCode);
    }

    [Fact]
    public async Task PaymentConfirmationReplay_UsesSameAdapterAndIdempotencyKey()
    {
        var transport = new RecordingHandler();
        var client = CreateClient(transport);
        var request = new VendorParkingFeeConfirmationRequest(null, "CARD-A", 1, 100, "PHP",
            Guid.NewGuid(), new(SiteA, Group, VendorA, AdapterA), "ACK-1");
        await client.ConfirmParkingFeeAsync(request, default);
        await client.ConfirmParkingFeeAsync(request, default);
        Assert.Equal(new[] { "adapter-a.internal", "adapter-a.internal" }, transport.Hosts);
        Assert.Equal(new[] { "ACK-1", "ACK-1" }, transport.IdempotencyKeys);
    }

    private static SiteVendorAdapterHttpClient CreateClient(RecordingHandler handler) =>
        new(new HttpClient(handler), new Registry(), new CredentialResolver(), Guid.NewGuid(), true);

    private static VendorParkingSessionLookupRequest Request(Guid site, Guid vendor) =>
        new(null, "CARD", Guid.NewGuid(), new(site, Group, vendor, Guid.Empty));

    private sealed class Registry : ISiteVendorAdapterRouteRegistry
    {
        public Task<SiteVendorAdapterRoute> ResolveAsync(Guid siteId, Guid siteGroupId, Guid? vendorSystemId,
            CancellationToken cancellationToken)
        {
            if (siteGroupId != Group ||
                (siteId == SiteA && vendorSystemId != VendorA) ||
                (siteId == SiteB && vendorSystemId != VendorB) ||
                (siteId != SiteA && siteId != SiteB))
                return Task.FromException<SiteVendorAdapterRoute>(
                    new SiteVendorAdapterRoutingException("SITE_ADAPTER_MAPPING_NOT_FOUND"));
            var route = siteId == SiteA
                ? new SiteVendorAdapterRoute(SiteA, Group, VendorA, AdapterA,
                    new Uri("http://adapter-a.internal"), "file:key-a", "IST", DateTimeOffset.UtcNow, null)
                : new SiteVendorAdapterRoute(SiteB, Group, VendorB, AdapterB,
                    new Uri("http://adapter-b.internal"), "file:key-b", "IST", DateTimeOffset.UtcNow, null);
            return Task.FromResult(route);
        }
    }

    private sealed class MissingRegistry : ISiteVendorAdapterRouteRegistry
    {
        public Task<SiteVendorAdapterRoute> ResolveAsync(Guid siteId, Guid siteGroupId, Guid? vendorSystemId,
            CancellationToken cancellationToken) =>
            Task.FromException<SiteVendorAdapterRoute>(new SiteVendorAdapterRoutingException("SITE_ADAPTER_MAPPING_NOT_FOUND"));
    }

    private sealed class CredentialResolver : ISiteAdapterCredentialResolver
    {
        public string Resolve(string credentialReference) => "test-service-key";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];
        public List<string> ServiceIdentities { get; } = [];
        public List<string> IdempotencyKeys { get; } = [];
        public Guid? OverrideAdapterIdentity { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            ServiceIdentities.Add(request.Headers.GetValues("X-ExitPass-Service-Identity").Single());
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var context = body.RootElement.GetProperty("context");
            if (body.RootElement.TryGetProperty("idempotencyKey", out var key))
                IdempotencyKeys.Add(key.GetString()!);
            var responseContext = new VendorAdapterResponseContext(
                Guid.Parse(context.GetProperty("siteId").GetString()!),
                Guid.Parse(context.GetProperty("siteGroupId").GetString()!),
                Guid.Parse(context.GetProperty("vendorSystemId").GetString()!),
                OverrideAdapterIdentity ?? Guid.Parse(context.GetProperty("adapterIdentityId").GetString()!), "1", "IST");
            object response = request.RequestUri.AbsolutePath.EndsWith("/parking-fees/confirm", StringComparison.Ordinal)
                ? new VendorParkingFeeConfirmationResponse(VendorParkingLookupStatus.Confirmed,
                    new(100, "PHP", DateTimeOffset.UtcNow), null, false,
                    Guid.Parse(body.RootElement.GetProperty("correlationId").GetString()!))
                    { AdapterContext = responseContext }
                : new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.Found,
                    new("HIKCENTRAL", "SESSION", "PLATE", DateTimeOffset.UtcNow, 1, "ACTIVE", null),
                    null, false, Guid.Parse(body.RootElement.GetProperty("correlationId").GetString()!))
                    { AdapterContext = responseContext };
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response),
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
