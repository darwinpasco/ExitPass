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
    private readonly string annexE1GovernedKeyFile = Path.Combine(Path.GetTempPath(), $"exitpass-annex-e1-governed-key-{Guid.NewGuid():N}.txt");
    private static readonly Guid Site = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Pos = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Identity = Guid.Parse("30000000-0000-4000-8000-000000000001");

    public FiscalReportingPosServerGatewayTests()
    {
        File.WriteAllText(keyFile, "pos-secret");
        File.WriteAllText(annexE1GovernedKeyFile, "annex-e1-governed-secret");
    }

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
    [InlineData(FiscalReportingGatewayAction.XDownload, null, "/v1/fiscal-reports/x-readings/X-20260915-001/exports/pdf", "application/pdf", "reading-57mm.pdf", "fiscal_x_reading.export")]
    [InlineData(FiscalReportingGatewayAction.XDownload, "pdf", "/v1/fiscal-reports/x-readings/X-20260915-001/exports/pdf", "application/pdf", "reading-57mm.pdf", "fiscal_x_reading.export")]
    [InlineData(FiscalReportingGatewayAction.XDownload, "json", "/v1/fiscal-reports/x-readings/X-20260915-001/exports/json", "application/json; charset=utf-8", "x-reading.json", "fiscal_x_reading.export")]
    [InlineData(FiscalReportingGatewayAction.ZDownload, null, "/v1/fiscal-reports/z-readings/Z-20260915-001/exports/pdf", "application/pdf", "reading-57mm.pdf", "fiscal_z_reading.export")]
    [InlineData(FiscalReportingGatewayAction.ZDownload, "pdf", "/v1/fiscal-reports/z-readings/Z-20260915-001/exports/pdf", "application/pdf", "reading-57mm.pdf", "fiscal_z_reading.export")]
    [InlineData(FiscalReportingGatewayAction.ZDownload, "json", "/v1/fiscal-reports/z-readings/Z-20260915-001/exports/json", "application/json; charset=utf-8", "z-reading.json", "fiscal_z_reading.export")]
    public async Task ReadingDownloadsRequestTheSelectedAuthoritativeExport(
        FiscalReportingGatewayAction action,
        string? format,
        string expectedPath,
        string contentType,
        string fileName,
        string expectedPermission)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(format == "json" ? "{}"u8.ToArray() : "%PDF-1.4"u8.ToArray())
        };
        response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = fileName };
        var handler = new CaptureHandler(_ => response);

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(action) with
            {
                ReportReference = action == FiscalReportingGatewayAction.XDownload ? "X-20260915-001" : "Z-20260915-001",
                ExportFormat = format
            });

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(fileName, result.FileName);
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
    [InlineData(FiscalReportingGatewayAction.BirSalesSummaryDownloadPdf, "pdf")]
    public async Task BirSalesSummaryExportsArePassedThroughWithAuthoritativeHash(
        FiscalReportingGatewayAction action,
        string format)
    {
        var summaryId = Guid.Parse("40000000-0000-4000-8000-000000000072");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("authoritative-export"u8.ToArray())
        };
        response.Content.Headers.ContentType = new(format switch { "json" => "application/json", "csv" => "text/csv", _ => "application/pdf" });
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
        await gateway.SendAsync(Request(FiscalReportingGatewayAction.AnnexE1DownloadPdf) with { WorkbookId = workbookId });

        Assert.Equal("/v1/fiscal-reports/annex-e/e1/workbooks", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("fiscal_annex_e.generate", Header(handler.Requests[0], "X-PosServer-Admin-Permission"));
        Assert.Contains("\"calendarYear\":2026", handler.Bodies[0]);
        Assert.Contains("\"calendarMonth\":9", handler.Bodies[0]);
        Assert.Contains("\"profile\":\"pos-server-bir-annex-e1-rmo24-2023:v1\"", handler.Bodies[0]);
        Assert.Equal($"/v1/fiscal-reports/annex-e/e1/workbooks/{workbookId:D}/content", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("fiscal_annex_e.export", Header(handler.Requests[1], "X-PosServer-Admin-Permission"));
        Assert.Equal($"/v1/fiscal-reports/annex-e/e1/workbooks/{workbookId:D}/content", handler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("?format=pdf", handler.Requests[2].RequestUri.Query);
        Assert.Equal("fiscal_annex_e.export", Header(handler.Requests[2], "X-PosServer-Admin-Permission"));
    }

    [Fact]
    public async Task AnnexE1KnownZeroUsesDedicatedCredentialAndExactPosContract()
    {
        var periodId = Guid.Parse("40000000-0000-4000-8000-000000000074");
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true,\"code\":\"annex_e1_fact_recorded\"}", HttpStatusCode.Created));

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.AnnexE1KnownZeroAttest) with
            {
                OperationKey = "oct17-naac-zero",
                FiscalReportingPeriodId = periodId,
                FactType = "naac_discount",
                FactStatus = "attested_zero",
                AmountMinorUnits = 0,
                SourceDocumentCount = 0,
                ApprovalReference = "FINANCE-APPROVAL-20261018-001"
            });

        Assert.Equal(201, result.HttpStatusCode);
        var outbound = Assert.Single(handler.Requests);
        Assert.Equal("/v1/fiscal-reports/annex-e/e1/known-zero-attestations", outbound.RequestUri!.AbsolutePath);
        Assert.Equal("annex-e1-governed-secret", Header(outbound, "X-PosServer-Admin-Key"));
        Assert.Equal("fiscal_annex_e.known_zero.attest", Header(outbound, "X-PosServer-Admin-Permission"));
        var body = handler.Bodies.Single();
        Assert.Contains($"\"sitePosServerId\":\"{Pos:D}\"", body);
        Assert.Contains($"\"fiscalIdentityId\":\"{Identity:D}\"", body);
        Assert.Contains($"\"fiscalReportingPeriodId\":\"{periodId:D}\"", body);
        Assert.Contains("\"currencyCode\":\"PHP\"", body);
        Assert.Contains("\"factType\":\"naac_discount\"", body);
        Assert.Contains("\"factStatus\":\"attested_zero\"", body);
        Assert.Contains("\"amountMinorUnits\":0", body);
        Assert.Contains("\"sourceDocumentCount\":0", body);
        Assert.Contains("\"approvalReference\":\"FINANCE-APPROVAL-20261018-001\"", body);
    }

    [Fact]
    public async Task AnnexE1CorrectionUsesDedicatedThreePermissionPrincipalWithoutNarrowingSelector()
    {
        var predecessor = Guid.Parse("613af721-3b65-4bc4-9ce3-d2a032c26ab3");
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true,\"code\":\"annex_e1_generated\"}", HttpStatusCode.Created));

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.AnnexE1Generate) with
            {
                OperationKey = "annex-e1-202610-r003",
                CalendarYear = 2026,
                CalendarMonth = 10,
                SupersedesWorkbookId = predecessor,
                CorrectionReason = "authorized_restatement",
                CorrectionApprovalReference = "FINANCE-APPROVAL-20261018-R003"
            });

        Assert.Equal(201, result.HttpStatusCode);
        var outbound = Assert.Single(handler.Requests);
        Assert.Equal("annex-e1-governed-secret", Header(outbound, "X-PosServer-Admin-Key"));
        Assert.False(outbound.Headers.Contains("X-PosServer-Admin-Permission"));
        var body = handler.Bodies.Single();
        Assert.Contains($"\"supersedesWorkbookId\":\"{predecessor:D}\"", body);
        Assert.Contains("\"correctionReason\":\"authorized_restatement\"", body);
        Assert.Contains("\"correctionApprovalReference\":\"FINANCE-APPROVAL-20261018-R003\"", body);
    }

    [Fact]
    public async Task OrdinaryAnnexE1GenerationRetainsNormalPrincipalAndSingleGenerateSelector()
    {
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true}", HttpStatusCode.Created));
        await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.AnnexE1Generate) with
            {
                OperationKey = "ordinary-annex-generation",
                CalendarYear = 2026,
                CalendarMonth = 10
            });

        var outbound = Assert.Single(handler.Requests);
        Assert.Equal("pos-secret", Header(outbound, "X-PosServer-Admin-Key"));
        Assert.Equal("fiscal_annex_e.generate", Header(outbound, "X-PosServer-Admin-Permission"));
    }

    [Fact]
    public async Task GovernedAnnexE1OperationsFailClosedWhenDedicatedCredentialIsUnavailable()
    {
        var endpoint = Endpoint(Site, "https://site-a.example/");
        endpoint.AnnexE1GovernedCorrectionApiKeyFile = null;
        var handler = new CaptureHandler(_ => throw new InvalidOperationException("must not send"));

        var result = await Gateway(handler, OptionsFor(endpoint)).SendAsync(
            Request(FiscalReportingGatewayAction.AnnexE1KnownZeroAttest));

        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal("site_pos_server_annex_e1_governed_api_key_file_unavailable", result.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnnexE1CurrentReadUsesServerResolvedScopeAndReadPermission()
    {
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true,\"workbook\":{}}"));
        var gateway = Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/")));

        await gateway.SendAsync(Request(FiscalReportingGatewayAction.AnnexE1CurrentRead) with
        {
            CalendarYear = 2026,
            CalendarMonth = 10
        });

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1/fiscal-reports/annex-e/e1/workbooks/current", request.RequestUri!.AbsolutePath);
        Assert.Contains($"sitePosServerId={Pos:D}", request.RequestUri.Query);
        Assert.Contains($"fiscalIdentityId={Identity:D}", request.RequestUri.Query);
        Assert.Contains("currencyCode=PHP", request.RequestUri.Query);
        Assert.Contains("calendarYear=2026", request.RequestUri.Query);
        Assert.Contains("calendarMonth=10", request.RequestUri.Query);
        Assert.Contains("profile=pos-server-bir-annex-e1-rmo24-2023%3Av1", request.RequestUri.Query);
        Assert.Equal("fiscal_annex_e.read", Header(request, "X-PosServer-Admin-Permission"));
    }

    [Fact]
    public async Task MonthlyESalesReadUsesServerResolvedFiscalScopeAndNarrowReadPermission()
    {
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true,\"report\":{}}"));

        await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.MonthlyESalesRead) with { CalendarYear = 2026, CalendarMonth = 9 });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1/fiscal-reports/monthly-esales/", request.RequestUri!.AbsolutePath);
        Assert.Contains($"sitePosServerId={Pos:D}", request.RequestUri.Query);
        Assert.Contains($"fiscalIdentityId={Identity:D}", request.RequestUri.Query);
        Assert.Contains("currencyCode=PHP", request.RequestUri.Query);
        Assert.Contains("calendarYear=2026", request.RequestUri.Query);
        Assert.Contains("calendarMonth=9", request.RequestUri.Query);
        Assert.Equal("monthly_esales.read", Header(request, "X-PosServer-Admin-Permission"));
    }

    [Theory]
    [InlineData(FiscalReportingGatewayAction.MonthlyESalesDownloadJson, "json", "application/json")]
    [InlineData(FiscalReportingGatewayAction.MonthlyESalesDownloadCsv, "csv", "text/csv")]
    [InlineData(FiscalReportingGatewayAction.MonthlyESalesDownloadPdf, "pdf", "application/pdf")]
    public async Task MonthlyESalesExportsProxyAuthoritativeBytesAndMetadata(
        FiscalReportingGatewayAction action,
        string format,
        string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes($"monthly-{format}"))
        };
        response.Content.Headers.ContentType = new(contentType);
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = $"monthly-esales-202609.{format}" };
        response.Headers.Add("X-Content-SHA256", "monthly-hash");
        var handler = new CaptureHandler(_ => response);

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(action) with { CalendarYear = 2026, CalendarMonth = 9 });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/fiscal-reports/monthly-esales/download", request.RequestUri!.AbsolutePath);
        Assert.Contains($"sitePosServerId={Pos:D}", request.RequestUri.Query);
        Assert.Contains($"fiscalIdentityId={Identity:D}", request.RequestUri.Query);
        Assert.Contains("format=" + format, request.RequestUri.Query);
        Assert.Equal("monthly_esales.export", Header(request, "X-PosServer-Admin-Permission"));
        Assert.Equal($"monthly-esales-202609.{format}", result.FileName);
        Assert.Equal("monthly-hash", result.ContentSha256);
        Assert.Equal(Encoding.UTF8.GetBytes($"monthly-{format}"), result.Body);
    }

    [Fact]
    public async Task AuditTrailReadUsesServerResolvedSiteScopeNarrowPermissionAndEncodedFilters()
    {
        var handler = new CaptureHandler(_ => Json("{\"succeeded\":true,\"report\":{\"rows\":[]}}"));
        await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.AuditTrailRead) with
            {
                PeriodStart = DateTimeOffset.Parse("2026-10-16T16:00:00Z"),
                PeriodEnd = DateTimeOffset.Parse("2026-10-17T16:00:00Z"),
                ActionCode = "fiscal_document_committed",
                ActorFilter = "System User",
                ResultFilter = "ALLOWED",
                ReferenceFilter = "SI-00000116"
            });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/fiscal-reports/audit-trail/", request.RequestUri!.AbsolutePath);
        Assert.Contains($"sitePosServerId={Pos:D}", request.RequestUri.Query);
        Assert.DoesNotContain(Identity.ToString("D"), request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from=2026-10-16T16%3A00%3A00.0000000%2B00%3A00", request.RequestUri.Query);
        Assert.Contains("to=2026-10-17T16%3A00%3A00.0000000%2B00%3A00", request.RequestUri.Query);
        Assert.Contains("actionCode=fiscal_document_committed", request.RequestUri.Query);
        Assert.Contains("actor=System%20User", request.RequestUri.Query);
        Assert.Contains("result=ALLOWED", request.RequestUri.Query);
        Assert.Contains("reference=SI-00000116", request.RequestUri.Query);
        Assert.Equal("fiscal_audit_trail.read", Header(request, "X-PosServer-Admin-Permission"));
        Assert.Equal("pos-secret", Header(request, "X-PosServer-Admin-Key"));
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("csv", "text/csv")]
    [InlineData("json", "application/json")]
    public async Task AuditTrailExportsProxyAuthoritativeBytesAndMetadata(string format, string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes($"audit-{format}"))
        };
        response.Content.Headers.ContentType = new(contentType);
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = $"audit-trail-20261017-20261017.{format}" };
        response.Headers.Add("X-Content-SHA256", "audit-hash");
        var handler = new CaptureHandler(_ => response);

        var result = await Gateway(handler, OptionsFor(Endpoint(Site, "https://site-a.example/"))).SendAsync(
            Request(FiscalReportingGatewayAction.AuditTrailDownload) with
            {
                PeriodStart = DateTimeOffset.Parse("2026-10-16T16:00:00Z"),
                PeriodEnd = DateTimeOffset.Parse("2026-10-17T16:00:00Z"),
                ExportFormat = format
            });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/fiscal-reports/audit-trail/download", request.RequestUri!.AbsolutePath);
        Assert.Contains("format=" + format, request.RequestUri.Query);
        Assert.Equal("fiscal_audit_trail.export", Header(request, "X-PosServer-Admin-Permission"));
        Assert.Equal($"audit-trail-20261017-20261017.{format}", result.FileName);
        Assert.Equal("audit-hash", result.ContentSha256);
        Assert.Equal(Encoding.UTF8.GetBytes($"audit-{format}"), result.Body);
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
        SitePosServerRef = $"POS-{site:D}-{url}", BaseUrl = url, ApiKeyFile = keyFile,
        AnnexE1GovernedCorrectionApiKeyFile = annexE1GovernedKeyFile, Environment = "Production", Enabled = enabled
    };
    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static string Header(HttpRequestMessage request, string name) => request.Headers.GetValues(name).Single();
    public void Dispose()
    {
        if (File.Exists(keyFile)) File.Delete(keyFile);
        if (File.Exists(annexE1GovernedKeyFile)) File.Delete(annexE1GovernedKeyFile);
    }

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
