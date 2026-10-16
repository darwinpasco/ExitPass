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
    public async Task CloseablePeriodsUsesTheMergedSiteScopedPosReadContract()
    {
        var handler = new CaptureHandler(_ => Json("{\"fiscalBusinessDates\":[]}"));

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.ZCloseablePeriods));

        Assert.Equal(200, result.HttpStatusCode);
        var outbound = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, outbound.Method);
        Assert.Equal("/v1/fiscal-reports/z-readings/closeable-periods", outbound.RequestUri!.AbsolutePath);
        Assert.Contains($"sitePosServerId={Pos:D}", outbound.RequestUri.Query);
        Assert.Contains($"fiscalIdentityId={Identity:D}", outbound.RequestUri.Query);
        Assert.Contains("currencyCode=PHP", outbound.RequestUri.Query);
        Assert.Equal("fiscal_z_reading.read", Header(outbound, "X-PosServer-Admin-Permission"));
    }

    [Fact]
    public async Task ClosePeriodUsesTheExistingSinglePeriodPosCloseContract()
    {
        var periodId = Guid.Parse("40000000-0000-4000-8000-000000000041");
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true}", HttpStatusCode.Created));

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.ZClosePeriod) with
            {
                OperationKey = "site-close-20260920",
                FiscalReportingPeriodId = periodId,
                ExpectedStateVersion = 17
            });

        Assert.Equal(201, result.HttpStatusCode);
        var outbound = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, outbound.Method);
        Assert.Equal("/v1/fiscal-reports/z-readings", outbound.RequestUri!.AbsolutePath);
        Assert.Equal("fiscal_z_reading.close", Header(outbound, "X-PosServer-Admin-Permission"));
        Assert.Contains($"\"fiscalReportingPeriodId\":\"{periodId:D}\"", handler.Bodies.Single());
        Assert.Contains("\"expectedStateVersion\":17", handler.Bodies.Single());
        Assert.Contains($"\"sitePosServerId\":\"{Pos:D}\"", handler.Bodies.Single());
    }

    [Theory]
    [InlineData(FiscalReportingGatewayAction.XDownload, "/v1/fiscal-reports/x-readings/X-20260915-001/exports/pdf", "fiscal_x_reading.export")]
    [InlineData(FiscalReportingGatewayAction.ZDownload, "/v1/fiscal-reports/z-readings/Z-20260915-001/exports/pdf", "fiscal_z_reading.export")]
    public async Task ReadingDownloadsRequestTheAuthoritativePdfExport(
        FiscalReportingGatewayAction action,
        string expectedPath,
        string expectedPermission)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("%PDF-1.4"u8.ToArray())
        };
        response.Content.Headers.ContentType = new("application/pdf");
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = "reading-57mm.pdf" };
        var handler = new CaptureHandler(_ => response);

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(action) with { ReportReference = action == FiscalReportingGatewayAction.XDownload ? "X-20260915-001" : "Z-20260915-001" });

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("reading-57mm.pdf", result.FileName);
        Assert.Equal(expectedPath, handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Empty(handler.Requests.Single().RequestUri!.Query);
        Assert.Equal(expectedPermission, Header(handler.Requests.Single(), "X-PosServer-Admin-Permission"));
    }

    [Fact]
    public async Task BirSalesSummaryGenerationUsesServerOwnedFiscalScopeAndDedicatedPosPermission()
    {
        var periodId = Guid.Parse("40000000-0000-4000-8000-000000000071");
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true,\"summary\":{}}", HttpStatusCode.Created));

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.BirSalesSummaryGenerate) with
            {
                OperationKey = "bir-summary-operation",
                FiscalReportingPeriodId = periodId,
                GoverningZReadingReference = "Z-20260915-001"
            });

        Assert.Equal(201, result.HttpStatusCode);
        var outbound = Assert.Single(handler.Requests);
        Assert.Equal("/v1/fiscal-reports/bir-sales-summaries/", outbound.RequestUri!.AbsolutePath);
        Assert.Equal("bir_sales_summary.generate", Header(outbound, "X-PosServer-Admin-Permission"));
        var body = handler.Bodies.Single();
        Assert.Contains($"\"sitePosServerId\":\"{Pos:D}\"", body);
        Assert.Contains($"\"fiscalIdentityId\":\"{Identity:D}\"", body);
        Assert.Contains("\"currencyCode\":\"PHP\"", body);
        Assert.Contains($"\"fiscalReportingPeriodId\":\"{periodId:D}\"", body);
        Assert.Contains("\"governingZReadingReference\":\"Z-20260915-001\"", body);
    }

    [Theory]
    [InlineData(FiscalReportingGatewayAction.BirSalesSummaryDownloadJson, "json")]
    [InlineData(FiscalReportingGatewayAction.BirSalesSummaryDownloadCsv, "csv")]
    public async Task BirSalesSummaryExportsArePassedThroughWithAuthoritativeHash(
        FiscalReportingGatewayAction action,
        string format)
    {
        var summaryId = Guid.Parse("40000000-0000-4000-8000-000000000072");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("authoritative-export"u8.ToArray())
        };
        response.Content.Headers.ContentType = new(format == "json" ? "application/json" : "text/csv");
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = $"bir-sales-summary.{format}" };
        response.Headers.Add("X-Content-SHA256", "fiscal-hash");
        var handler = new CaptureHandler(_ => response);

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(action) with { SummaryId = summaryId });

        Assert.Equal($"/v1/fiscal-reports/bir-sales-summaries/{summaryId:D}/exports/{format}", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Equal("bir_sales_summary.export", Header(handler.Requests.Single(), "X-PosServer-Admin-Permission"));
        Assert.Equal("fiscal-hash", result.ContentSha256);
        Assert.Equal($"bir-sales-summary.{format}", result.FileName);
        Assert.Equal("authoritative-export"u8.ToArray(), result.Body);
    }

    [Fact]
    public async Task AnnexE1GenerationAndDownloadUseTheExistingPosWorkbookContract()
    {
        var workbookId = Guid.Parse("40000000-0000-4000-8000-000000000073");
        var handler = new CaptureHandler(request => request.Method == HttpMethod.Post
            ? Json("{\"succeeded\":true,\"workbook\":{}}", HttpStatusCode.Created)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("xlsx-bytes"u8.ToArray())
                {
                    Headers = { ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") }
                }
            });
        var gateway = Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/")));

        await gateway.SendAsync(Request(FiscalReportingGatewayAction.AnnexE1Generate) with
        {
            OperationKey = "annex-operation",
            CalendarYear = 2026,
            CalendarMonth = 9
        });
        await gateway.SendAsync(Request(FiscalReportingGatewayAction.AnnexE1Download) with { WorkbookId = workbookId });

        Assert.Equal("/v1/fiscal-reports/annex-e/e1/workbooks", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("fiscal_annex_e.generate", Header(handler.Requests[0], "X-PosServer-Admin-Permission"));
        Assert.Contains("\"calendarYear\":2026", handler.Bodies[0]);
        Assert.Contains("\"calendarMonth\":9", handler.Bodies[0]);
        Assert.Contains("\"profile\":\"pos-server-bir-annex-e1-rmo24-2023:v1\"", handler.Bodies[0]);
        Assert.Equal($"/v1/fiscal-reports/annex-e/e1/workbooks/{workbookId:D}/content", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("fiscal_annex_e.export", Header(handler.Requests[1], "X-PosServer-Admin-Permission"));
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
