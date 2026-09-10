using System.Net;
using System.Text;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;
using ExitPass.CentralPms.Infrastructure.FiscalReporting;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalReporting;

public sealed class FiscalReportingPosServerGatewayTests : IDisposable
{
    private readonly string keyFile = Path.Combine(Path.GetTempPath(), $"exitpass-g4-pos-key-{Guid.NewGuid():N}.txt");
    private static readonly Guid Site = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Pos = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Identity = Guid.Parse("30000000-0000-4000-8000-000000000001");

    public FiscalReportingPosServerGatewayTests() => File.WriteAllText(keyFile, "pos-secret");

    [Fact]
    public async Task CorrectSiteBindingIsSelectedAndCentralAddsTheLeastPrivilegePosCredential()
    {
        var handler = new CaptureHandler(_ => Json("{\"invoices\":[]}"));
        var gateway = Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"), Endpoint(Guid.NewGuid(), "https://site-b.example/")));

        var result = await gateway.SendAsync(Request(FiscalReportingGatewayAction.ElectronicJournalRead) with
        {
            PeriodStart = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            PeriodEnd = DateTimeOffset.Parse("2026-09-02T00:00:00Z"),
            Search = "SI-0001"
        });

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("site-a.example", handler.Requests.Single().RequestUri!.Host);
        Assert.Contains("fiscalDocumentNumber=SI-0001", handler.Requests.Single().RequestUri!.Query);
        Assert.Equal("pos-secret", Header(handler.Requests.Single(), "X-PosServer-Admin-Key"));
        Assert.Equal("electronic_journal.read", Header(handler.Requests.Single(), "X-PosServer-Admin-Permission"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("inactive")]
    public async Task InvalidSiteBindingsFailClosedWithoutContactingPos(string scenario)
    {
        var handler = new CaptureHandler(_ => throw new InvalidOperationException("must not send"));
        var endpoints = scenario switch
        {
            "missing" => Array.Empty<SitePosServerEndpointOptions>(),
            "ambiguous" => new[] { Endpoint(Site, "https://one.example/"), Endpoint(Site, "https://two.example/") },
            _ => new[] { Endpoint(Site, "https://one.example/", enabled: false) }
        };
        var result = await Gateway(handler, OptionsFor(endpoints)).SendAsync(Request(FiscalReportingGatewayAction.XHistory));

        Assert.Equal(503, result.HttpStatusCode);
        Assert.Empty(handler.Requests);
        Assert.Contains(scenario == "missing" ? "not_found" : scenario, result.Code);
    }

    [Fact]
    public async Task ZGenerationObtainsTheCurrentPosOwnedPeriodBeforeClosingIt()
    {
        var handler = new CaptureHandler(request => request.Method == HttpMethod.Get
            ? Json("{\"currentPeriod\":{\"fiscalReportingPeriodId\":\"40000000-0000-4000-8000-000000000001\",\"expectedZStateVersion\":7},\"readings\":[]}")
            : Json("{\"succeeded\":true,\"zReading\":{}}", HttpStatusCode.Created));
        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.ZGenerate) with { OperationKey = "z-operation" });

        Assert.Equal(201, result.HttpStatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/v1/fiscal-reports/z-readings/history", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("\"fiscalReportingPeriodId\":\"40000000-0000-4000-8000-000000000001\"", handler.Bodies[1]);
        Assert.Contains("\"expectedStateVersion\":7", handler.Bodies[1]);
    }

    [Fact]
    public async Task UnavailablePosFailsCleanlyAndIsRetryable()
    {
        var gateway = Gateway(new ThrowingHandler(), OptionsFor(Endpoint(Site, "https://site-a.example/")));
        var result = await gateway.SendAsync(Request(FiscalReportingGatewayAction.XHistory));
        Assert.Equal(503, result.HttpStatusCode);
        Assert.True(result.Retryable);
        Assert.Equal("site_pos_server_unavailable", result.Code);
    }

    private HttpFiscalReportingPosServerGateway Gateway(HttpMessageHandler handler, FiscalIssuancePosServerIntegrationOptions options) =>
        new(new HttpClient(handler), new ConfiguredSitePosServerBindingResolver(options));
    private FiscalReportingGatewayRequest Request(FiscalReportingGatewayAction action) => new(Site, action, Guid.NewGuid(), "actor");
    private FiscalIssuancePosServerIntegrationOptions OptionsFor(params SitePosServerEndpointOptions[] endpoints) =>
        new() { RuntimeEnvironment = "Production", Endpoints = [.. endpoints] };
    private SitePosServerEndpointOptions Endpoint(Guid site, string url, bool enabled = true) => new()
    {
        SiteId = site, SitePosServerId = site == Site ? Pos : Guid.NewGuid(), FiscalIdentityId = Identity, CurrencyCode = "PHP",
        SitePosServerRef = $"POS-{site:D}-{url}", BaseUrl = url, ApiKeyFile = keyFile, Environment = "Production", Enabled = enabled
    };
    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static string Header(HttpRequestMessage request, string name) => request.Headers.GetValues(name).Single();
    public void Dispose() { if (File.Exists(keyFile)) File.Delete(keyFile); }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return response(request);
        }
    }
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
