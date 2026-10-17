using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleFiscalReportingApiIntegrationTests
{
    private const string Scheme = "FiscalReportingTest";
    private static readonly Guid UserId = Guid.Parse("88000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("88000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherSiteId = Guid.Parse("88000000-0000-0000-0000-000000000003");
    private const string Ticket = "MOCK-USABILITY-20260924-01";
    private const string Plate = "USR2401";
    private const string SalesInvoice = "SI-00000049";

    [Theory]
    [InlineData(Ticket)]
    [InlineData(Plate)]
    [InlineData(SalesInvoice)]
    public async Task ElectronicJournal_ExactOperatorIdentifier_ResolvesToAuthoritativeSalesInvoice(string identifier)
    {
        var gateway = new FakeGateway();
        var references = new FakeReferenceRepository();
        using var factory = CreateFactory(gateway, references, role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, identifier));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        gateway.CallCount.Should().Be(1);
        gateway.LastRequest!.Search.Should().Be(SalesInvoice);
        references.LastIdentifier.Should().Be(identifier);
        references.LastSiteId.Should().Be(SiteId);
    }

    [Fact]
    public async Task ElectronicJournal_UnknownIdentifier_ReturnsControlledEmptyResultWithoutCallingPos()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, "UNKNOWN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"invoices\":");
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_AmbiguousIdentifier_ReturnsConflictWithoutCallingPos()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, "AMBIGUOUS"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fiscal_reporting_identifier_ambiguous");
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_SiteOperatorWithStalePermission_IsDenied()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "SITE_OPERATOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("OPERATOR_CONSOLE_FISCAL_REPORTING_ROLE_REQUIRED");
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DigitalSalesInvoice_SiteOperatorWithStatusPermission_IsRoleDenied()
    {
        using var factory = CreateFactory(
            new FakeGateway(),
            new FakeReferenceRepository(),
            role: "SITE_OPERATOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-issuance/references/{Guid.NewGuid():D}/digital-sales-invoice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain("OPERATOR_CONSOLE_FISCAL_REPORTING_ROLE_REQUIRED");
    }

    [Fact]
    public async Task ElectronicJournal_OutsideAuthenticatedSiteScope_IsConcealed()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(OtherSiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_MissingPermission_IsDenied()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(
            gateway,
            new FakeReferenceRepository(),
            role: "OPERATIONS_SUPERVISOR",
            includePermission: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_Unauthenticated_IsDenied()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(
            gateway,
            new FakeReferenceRepository(),
            role: "OPERATIONS_SUPERVISOR",
            authenticated: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        gateway.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("x", null, FiscalReportingGatewayAction.XDownload, "pdf")]
    [InlineData("x", "pdf", FiscalReportingGatewayAction.XDownload, "pdf")]
    [InlineData("x", "json", FiscalReportingGatewayAction.XDownload, "json")]
    [InlineData("z", null, FiscalReportingGatewayAction.ZDownload, "pdf")]
    [InlineData("z", "pdf", FiscalReportingGatewayAction.ZDownload, "pdf")]
    [InlineData("z", "json", FiscalReportingGatewayAction.ZDownload, "json")]
    public async Task ReadingDownload_UsesValidatedFormatAndPreservesTheSiteScopedGateway(
        string kind,string? format,FiscalReportingGatewayAction expectedAction,string expectedFormat)
    {
        var contentType=expectedFormat=="json"?"application/json; charset=utf-8":"application/pdf";
        var fileName=$"reading.{expectedFormat}";
        var gateway=new FakeGateway(_=>new FiscalReportingGatewayResponse(
            200,contentType,Encoding.UTF8.GetBytes(expectedFormat=="json"?"{}":"%PDF-1.4"),"ok",fileName));
        using var factory=CreateFactory(gateway,new FakeReferenceRepository(),role:"OPERATIONS_SUPERVISOR");
        using var client=factory.CreateClient();
        var suffix=format is null?string.Empty:$"?format={format}";

        using var response=await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/{kind}-readings/{kind.ToUpperInvariant()}-20261017-001/download{suffix}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be(contentType);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be(fileName);
        var request=gateway.Requests.Should().ContainSingle().Subject;
        request.SiteId.Should().Be(SiteId);
        request.Action.Should().Be(expectedAction);
        request.ExportFormat.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("z")]
    public async Task ReadingDownload_RejectsUnsupportedFormatBeforeCallingPos(string kind)
    {
        var gateway=new FakeGateway();
        using var factory=CreateFactory(gateway,new FakeReferenceRepository(),role:"OPERATIONS_SUPERVISOR");
        using var client=factory.CreateClient();

        using var response=await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/{kind}-readings/{kind.ToUpperInvariant()}-20261017-001/download?format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fiscal_report_export_format_unsupported");
        gateway.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("z")]
    public async Task ReadingDownload_ConcealsOutOfScopeSiteBeforeFormatValidation(string kind)
    {
        var gateway=new FakeGateway();
        using var factory=CreateFactory(gateway,new FakeReferenceRepository(),role:"OPERATIONS_SUPERVISOR");
        using var client=factory.CreateClient();

        using var response=await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/{kind}-readings/{kind.ToUpperInvariant()}-20261017-001/download?format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CloseablePeriods_UsesExactlyTheAuthenticatedSiteScope()
    {
        var gateway = new FakeGateway(request => Response(200, CloseablePeriodsJson(
            Period("2026-09-20", 10),
            Period("2026-09-21", 11))));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        gateway.Requests.Should().ContainSingle();
        gateway.Requests[0].SiteId.Should().Be(SiteId);
        gateway.Requests[0].Action.Should().Be(FiscalReportingGatewayAction.ZCloseablePeriods);
    }

    [Fact]
    public async Task CloseOne_UsesThePosPeriodAndExpectedVersionWithoutEnumeratingAnotherSite()
    {
        var periodId = Guid.Parse("88000000-0000-0000-0000-000000000021");
        var gateway = new FakeGateway(_ => Response(201, "{\"succeeded\":true,\"birSalesSummaryCompletion\":{\"status\":\"PENDING\",\"code\":\"bir_sales_summary_persistence_failed\",\"retryable\":true}}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/{periodId:D}/close",
            new { operationKey = "close-one", expectedStateVersion = 23, confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"PENDING\"");
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.SiteId.Should().Be(SiteId);
        request.Action.Should().Be(FiscalReportingGatewayAction.ZClosePeriod);
        request.FiscalReportingPeriodId.Should().Be(periodId);
        request.ExpectedStateVersion.Should().Be(23);
        gateway.Requests.Should().NotContain(item => item.SiteId == OtherSiteId);
    }

    [Fact]
    public async Task CloseOne_PlainTextUpstreamException_ReturnsControlledJsonWithoutDiagnostics()
    {
        var periodId = Guid.Parse("88000000-0000-0000-0000-000000000022");
        var gateway = new FakeGateway(_ => new FiscalReportingGatewayResponse(
            500,
            "text/plain",
            Encoding.UTF8.GetBytes("ExitPass.PosServer.Runtime.InternalException: private stack trace"),
            "upstream_failure"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/{periodId:D}/close",
            new { operationKey = "controlled-error", expectedStateVersion = 7, confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("fiscal_reporting_upstream_operation_failed");
        body.Should().Contain("The authoritative fiscal reporting operation could not be completed.");
        body.Should().NotContain("ExitPass.PosServer");
        body.Should().NotContain("stack trace");
    }

    [Fact]
    public async Task CloseAll_ProcessesOldestFirstAndStopsAtTheFirstFailure()
    {
        var periods = new[]
        {
            Period("2026-09-20", 10),
            Period("2026-09-21", 11),
            Period("2026-09-22", 12)
        };
        var closeCalls = 0;
        var gateway = new FakeGateway(request => request.Action switch
        {
            FiscalReportingGatewayAction.ZCloseablePeriods => Response(200, CloseablePeriodsJson(periods)),
            FiscalReportingGatewayAction.ZClosePeriod when ++closeCalls == 1 => Response(201, "{\"succeeded\":true}"),
            FiscalReportingGatewayAction.ZClosePeriod => Response(409, "{\"succeeded\":false,\"code\":\"stale_state_version\"}"),
            _ => throw new InvalidOperationException("Unexpected gateway action.")
        });
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/close-all",
            new { operationKey = "close-all", confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"closedCount\":1");
        body.Should().Contain("2026-09-21");
        gateway.Requests.Select(item => item.Action).Should().Equal(
            FiscalReportingGatewayAction.ZCloseablePeriods,
            FiscalReportingGatewayAction.ZClosePeriod,
            FiscalReportingGatewayAction.ZClosePeriod,
            FiscalReportingGatewayAction.ZCloseablePeriods);
        gateway.Requests.Where(item => item.Action == FiscalReportingGatewayAction.ZClosePeriod)
            .Select(item => item.FiscalReportingPeriodId)
            .Should().Equal(periods[0].FiscalReportingPeriodId, periods[1].FiscalReportingPeriodId);
        gateway.Requests.Should().OnlyContain(item => item.SiteId == SiteId);
    }

    [Fact]
    public async Task CloseAll_PreservesSuccessfulZResultsAndReportsPendingBirCompletions()
    {
        var periods = new[] { Period("2026-09-20", 10), Period("2026-09-21", 11) };
        var closeCalls = 0;
        var gateway = new FakeGateway(request => request.Action switch
        {
            FiscalReportingGatewayAction.ZCloseablePeriods => Response(200, CloseablePeriodsJson(periods)),
            FiscalReportingGatewayAction.ZClosePeriod when ++closeCalls == 1 => Response(201, "{\"succeeded\":true,\"birSalesSummaryCompletion\":{\"status\":\"COMMITTED\",\"summaryId\":\"88000000-0000-0000-0000-000000000099\",\"code\":\"bir_sales_summary_committed\",\"retryable\":false}}"),
            FiscalReportingGatewayAction.ZClosePeriod => Response(201, "{\"succeeded\":true,\"birSalesSummaryCompletion\":{\"status\":\"PENDING\",\"code\":\"bir_sales_summary_persistence_failed\",\"retryable\":true}}"),
            _ => throw new InvalidOperationException("Unexpected gateway action.")
        });
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/close-all",
            new { operationKey = "close-all-pending-bir", confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"closedCount\":2");
        body.Should().Contain("\"birSalesSummaryPendingCount\":1");
        body.Should().Contain("Daily BIR Sales Summary operation(s) are pending");
    }

    [Fact]
    public async Task CloseAll_OutsideAuthenticatedSiteScope_IsConcealedBeforePosIsCalled()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/z-readings/closeable-periods/close-all",
            new { operationKey = "wrong-site", confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BirSalesSummaryGenerationUsesOnlyAuthenticatedSiteAndSelectedCommittedZ()
    {
        var periodId = Guid.Parse("88000000-0000-0000-0000-000000000031");
        var gateway = new FakeGateway(_ => Response(201, "{\"succeeded\":true,\"summary\":{}}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-Token", TestAntiforgery.ValidToken);

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/bir-sales-summaries",
            new
            {
                operationKey = "bir-summary",
                fiscalReportingPeriodId = periodId,
                governingZReadingReference = "Z-20260920-001",
                sitePosServerId = OtherSiteId,
                fiscalIdentityId = OtherSiteId
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.SiteId.Should().Be(SiteId);
        request.Action.Should().Be(FiscalReportingGatewayAction.BirSalesSummaryGenerate);
        request.FiscalReportingPeriodId.Should().Be(periodId);
        request.GoverningZReadingReference.Should().Be("Z-20260920-001");
    }

    [Fact]
    public async Task BirSalesSummaryOutsideAuthenticatedSiteScopeIsConcealed()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/bir-sales-summaries/{Guid.NewGuid():D}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BirSalesSummaryGenerationRequiresCsrfValidation()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/bir-sales-summaries",
            new { operationKey = "missing-csrf", fiscalReportingPeriodId = Guid.NewGuid(), governingZReadingReference = "Z-CSRF" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CSRF_VALIDATION_FAILED");
        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnnexE1GenerationRequiresCsrfValidation()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/annex-e/e1/workbooks",
            new { operationKey = "missing-csrf", calendarYear = 2026, calendarMonth = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CSRF_VALIDATION_FAILED");
        gateway.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bir_sales_summary_period_not_closed", "The BIR Sales Summary can be generated after the fiscal business date has been closed and its Z Reading has been committed.")]
    [InlineData("bir_sales_summary_z_not_committed", "The BIR Sales Summary is unavailable until the governing Z Reading is committed.")]
    [InlineData("bir_sales_summary_reconciliation_failed", "The authoritative fiscal report did not reconcile.")]
    public async Task BirSalesSummarySafePosFailureIsPropagatedWithoutDiagnostics(string code, string message)
    {
        var gateway = new FakeGateway(_ => Response(409, $"{{\"succeeded\":false,\"code\":\"{code}\",\"message\":\"{message}\"}}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-Token", TestAntiforgery.ValidToken);

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/bir-sales-summaries",
            new { operationKey = "safe-failure", fiscalReportingPeriodId = Guid.NewGuid(), governingZReadingReference = "Z-FAILED" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(code).And.Contain(message).And.NotContain("stack trace");
    }

    [Theory]
    [InlineData("json", "application/json", "bir-summary.json")]
    [InlineData("csv", "text/csv", "bir-summary.csv")]
    [InlineData("pdf", "application/pdf", "bir-summary.pdf")]
    public async Task BirSalesSummaryExportProxiesAuthoritativeBytesAndMetadata(string format, string contentType, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes($"authoritative-{format}");
        var gateway = new FakeGateway(_ => new FiscalReportingGatewayResponse(200, contentType, bytes, "ok", fileName, ContentSha256: "sha256-value"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/bir-sales-summaries/{Guid.NewGuid():D}/download?format={format}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be(fileName);
        response.Headers.GetValues("X-Content-SHA256").Should().ContainSingle("sha256-value");
    }

    [Fact]
    public async Task AnnexE1GenerationAndDownloadUseTheSiteScopedGatewayWithoutByteConversion()
    {
        var workbookId = Guid.Parse("88000000-0000-0000-0000-000000000032");
        var bytes = "authoritative-xlsx"u8.ToArray();
        var gateway = new FakeGateway(request => request.Action == FiscalReportingGatewayAction.AnnexE1Download
            ? new(200, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bytes, "ok", "annex-e1.xlsx", ContentSha256: "xlsx-hash")
            : Response(201, $"{{\"succeeded\":true,\"workbook\":{{\"workbookId\":\"{workbookId:D}\"}}}}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CSRF-Token", TestAntiforgery.ValidToken);

        using var generation = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/annex-e/e1/workbooks",
            new { operationKey = "annex", calendarYear = 2026, calendarMonth = 9, fiscalIdentityId = OtherSiteId });
        using var download = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/annex-e/e1/workbooks/{workbookId:D}/download");

        generation.StatusCode.Should().Be(HttpStatusCode.Created);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        gateway.Requests.Should().OnlyContain(request => request.SiteId == SiteId);
        gateway.Requests[0].CalendarYear.Should().Be(2026);
        gateway.Requests[0].CalendarMonth.Should().Be(9);
        gateway.Requests[1].WorkbookId.Should().Be(workbookId);
    }

    [Fact]
    public async Task AnnexE1PdfDownloadUsesTheExistingWorkbookAndPreservesAuthoritativeBytes()
    {
        var workbookId = Guid.Parse("88000000-0000-0000-0000-000000000032");
        var bytes = "%PDF-1.4 annex"u8.ToArray();
        var gateway = new FakeGateway(_ => new(200, "application/pdf", bytes, "ok", "annex-e1.pdf", ContentSha256: "pdf-hash"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/annex-e/e1/workbooks/{workbookId:D}/download?format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.Action.Should().Be(FiscalReportingGatewayAction.AnnexE1DownloadPdf);
        request.WorkbookId.Should().Be(workbookId);
        request.SiteId.Should().Be(SiteId);
    }

    [Fact]
    public async Task AnnexE1CurrentWorkbookReadIsSiteScopedAndPreservesNoCurrent()
    {
        var workbookId = Guid.Parse("613af721-3b65-4bc4-9ce3-d2a032c26ab3");
        var gateway = new FakeGateway(request => request.CalendarMonth == 10
            ? Response(200, $"{{\"succeeded\":true,\"workbook\":{{\"workbookId\":\"{workbookId:D}\",\"revision\":2}}}}")
            : Response(404, "{\"succeeded\":false,\"code\":\"annex_e1_not_found\"}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var current = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/annex-e/e1/workbooks/current?calendarYear=2026&calendarMonth=10");
        using var missing = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/annex-e/e1/workbooks/current?calendarYear=2026&calendarMonth=11");
        using var crossSite = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/annex-e/e1/workbooks/current?calendarYear=2026&calendarMonth=10");

        current.StatusCode.Should().Be(HttpStatusCode.OK);
        (await current.Content.ReadAsStringAsync()).Should().Contain(workbookId.ToString("D")).And.Contain("\"revision\":2");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crossSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.Requests.Should().HaveCount(2);
        gateway.Requests.Should().OnlyContain(request =>
            request.Action == FiscalReportingGatewayAction.AnnexE1CurrentRead &&
            request.SiteId == SiteId &&
            request.CalendarYear == 2026);
    }

    [Fact]
    public async Task MonthlyESalesReadUsesOnlyAuthenticatedSiteAndReadPermission()
    {
        var gateway = new FakeGateway(_ => Response(200, "{\"succeeded\":true,\"report\":{\"calendarYear\":2026,\"calendarMonth\":9}}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/monthly-esales?calendarYear=2026&calendarMonth=9&sitePosServerId={OtherSiteId:D}&fiscalIdentityId={OtherSiteId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.Action.Should().Be(FiscalReportingGatewayAction.MonthlyESalesRead);
        request.SiteId.Should().Be(SiteId);
        request.CalendarYear.Should().Be(2026);
        request.CalendarMonth.Should().Be(9);
        request.OperationKey.Should().BeNull();
    }

    [Fact]
    public async Task MonthlyESalesOutsideAuthenticatedSiteScopeIsConcealed()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/monthly-esales?calendarYear=2026&calendarMonth=9");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task MonthlyESalesReadRequiresExistingFiscalReportingReadPermission()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR", includePermission: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/monthly-esales?calendarYear=2026&calendarMonth=9");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("json", "application/json", "monthly-esales-technical-uat-202609.json")]
    [InlineData("csv", "text/csv", "monthly-esales-technical-uat-202609.csv")]
    [InlineData("pdf", "application/pdf", "monthly-esales-technical-uat-202609.pdf")]
    public async Task MonthlyESalesDownloadPreservesAuthoritativeBytesAndMetadata(string format, string contentType, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes($"authoritative-monthly-{format}");
        var gateway = new FakeGateway(_ => new FiscalReportingGatewayResponse(200, contentType, bytes, "ok", fileName, ContentSha256: "monthly-sha"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/monthly-esales/download?calendarYear=2026&calendarMonth=9&format={format}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be(fileName);
        response.Headers.GetValues("X-Content-SHA256").Should().ContainSingle("monthly-sha");
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.Action.Should().Be(format switch
        {
            "json" => FiscalReportingGatewayAction.MonthlyESalesDownloadJson,
            "csv" => FiscalReportingGatewayAction.MonthlyESalesDownloadCsv,
            "pdf" => FiscalReportingGatewayAction.MonthlyESalesDownloadPdf,
            _ => throw new InvalidOperationException($"Unexpected test format '{format}'.")
        });
        request.SiteId.Should().Be(SiteId);
    }

    [Fact]
    public async Task AuditTrailReadIsBoundedSiteScopedAndPassesSafeFiltersOnly()
    {
        var gateway = new FakeGateway(_ => Response(200, "{\"succeeded\":true,\"report\":{\"rows\":[]}}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();
        var url = $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/audit-trail" +
                  "?from=2026-10-16T16%3A00%3A00Z&to=2026-10-17T16%3A00%3A00Z" +
                  "&actionCode=fiscal_document_committed&actor=System&result=ALLOWED&reference=SI-00000116" +
                  $"&sitePosServerId={OtherSiteId:D}";

        using var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.Action.Should().Be(FiscalReportingGatewayAction.AuditTrailRead);
        request.SiteId.Should().Be(SiteId);
        request.PeriodStart.Should().Be(DateTimeOffset.Parse("2026-10-16T16:00:00Z"));
        request.PeriodEnd.Should().Be(DateTimeOffset.Parse("2026-10-17T16:00:00Z"));
        request.ActionCode.Should().Be("fiscal_document_committed");
        request.ActorFilter.Should().Be("System");
        request.ResultFilter.Should().Be("ALLOWED");
        request.ReferenceFilter.Should().Be("SI-00000116");
    }

    [Theory]
    [InlineData("pdf", "application/pdf", "audit-trail-20261017-20261017.pdf")]
    [InlineData("csv", "text/csv", "audit-trail-20261017-20261017.csv")]
    [InlineData("json", "application/json", "audit-trail-20261017-20261017.json")]
    public async Task AuditTrailDownloadPreservesAuthoritativeBytesAndMetadata(string format, string contentType, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes($"authoritative-audit-{format}");
        var gateway = new FakeGateway(_ => new FiscalReportingGatewayResponse(200, contentType, bytes, "ok", fileName, ContentSha256: "audit-sha"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/audit-trail/download?from=2026-10-16T16%3A00%3A00Z&to=2026-10-17T16%3A00%3A00Z&format={format}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be(fileName);
        response.Headers.GetValues("X-Content-SHA256").Should().ContainSingle("audit-sha");
        gateway.Requests.Should().ContainSingle().Which.Action.Should().Be(FiscalReportingGatewayAction.AuditTrailDownload);
        gateway.Requests[0].ExportFormat.Should().Be(format);
    }

    [Theory]
    [InlineData("/audit-trail")]
    [InlineData("/audit-trail/download?format=pdf")]
    public async Task AuditTrailRejectsUnboundedAndCrossSiteRequestsBeforeCallingPos(string suffix)
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var unbounded = await client.GetAsync($"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}{suffix}");
        using var crossSite = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/audit-trail?from=2026-10-16T16%3A00%3A00Z&to=2026-10-17T16%3A00%3A00Z");

        unbounded.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        crossSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AuditTrailDownloadRejectsUnsupportedFormatBeforeCallingPos()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/audit-trail/download?from=2026-10-16T16%3A00%3A00Z&to=2026-10-17T16%3A00%3A00Z&format=xml");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        gateway.Requests.Should().BeEmpty();
    }

    private static string EjUrl(Guid siteId, string identifier) =>
        $"/v1/ops/operator-console/fiscal-reporting/sites/{siteId:D}/electronic-journal" +
        $"?periodStart=2026-09-23T00%3A00%3A00Z&periodEnd=2026-09-25T00%3A00%3A00Z&search={Uri.EscapeDataString(identifier)}";

    private static CloseableTestPeriod Period(string businessDate, long version) =>
        new(Guid.Parse($"88000000-0000-0000-0000-{version:000000000000}"), businessDate, version);

    private static string CloseablePeriodsJson(params CloseableTestPeriod[] periods) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            succeeded = true,
            fiscalBusinessDates = periods.Select(period => new
            {
                fiscalReportingPeriodId = period.FiscalReportingPeriodId,
                fiscalBusinessDate = period.FiscalBusinessDate,
                periodStart = $"{period.FiscalBusinessDate}T00:00:00Z",
                periodEnd = $"{period.FiscalBusinessDate}T23:59:59Z",
                status = "CLOSEABLE",
                transactionCount = 1,
                expectedStateVersion = period.Version
            })
        });

    private static FiscalReportingGatewayResponse Response(int status, string body) =>
        new(status, "application/json", Encoding.UTF8.GetBytes(body), status < 400 ? "ok" : "failed");

    private static CustomWebApplicationFactory CreateFactory(
        FakeGateway gateway,
        FakeReferenceRepository references,
        string role,
        bool includePermission = true,
        bool authenticated = true) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowFixtureIdentityHeaders"] = "false"
            })
            .WithServiceOverrides(services =>
            {
                services.AddSingleton(new TestPrincipalOptions(role, includePermission, authenticated));
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = Scheme;
                        options.DefaultChallengeScheme = Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(Scheme, _ => { });
                services.RemoveAll<IFiscalReportingPosServerGateway>();
                services.AddSingleton<IFiscalReportingPosServerGateway>(gateway);
                services.RemoveAll<IFiscalIssuanceReferenceRepository>();
                services.AddSingleton<IFiscalIssuanceReferenceRepository>(references);
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository(role));
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton<IAntiforgery, TestAntiforgery>();
            });

    private sealed record TestPrincipalOptions(string Role, bool IncludePermission, bool Authenticated);

    private sealed class TestAntiforgery : IAntiforgery
    {
        public const string ValidToken = "fiscal-reporting-csrf";

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => GetTokens(httpContext);
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
            new(ValidToken, "test-cookie", "__RequestVerificationToken", "X-CSRF-Token");
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) =>
            Task.FromResult(httpContext.Request.Headers["X-CSRF-Token"] == ValidToken);
        public Task ValidateRequestAsync(HttpContext httpContext) =>
            httpContext.Request.Headers["X-CSRF-Token"] == ValidToken
                ? Task.CompletedTask
                : Task.FromException(new AntiforgeryValidationException("The test CSRF token is invalid."));
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly TestPrincipalOptions _principal;

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TestPrincipalOptions principal)
            : base(options, logger, encoder)
        {
            _principal = principal;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!_principal.Authenticated)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, UserId.ToString("D")),
                new(ClaimTypes.Role, _principal.Role),
                new("exitpass_audience", "OPERATOR_CONSOLE"),
                new("site_id", SiteId.ToString("D"))
            };
            if (_principal.IncludePermission)
            {
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.ej.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-issuance.status.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.x.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.z.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.z.generate"));
            }

            var identity = new ClaimsIdentity(claims, OperatorConsoleFiscalReportingApiIntegrationTests.Scheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity),
                    OperatorConsoleFiscalReportingApiIntegrationTests.Scheme)));
        }
    }

    private sealed class FakeGateway(Func<FiscalReportingGatewayRequest, FiscalReportingGatewayResponse>? response = null) : IFiscalReportingPosServerGateway
    {
        public List<FiscalReportingGatewayRequest> Requests { get; } = [];
        public int CallCount => Requests.Count;
        public FiscalReportingGatewayRequest? LastRequest => Requests.LastOrDefault();

        public Task<FiscalReportingGatewayResponse> SendAsync(
            FiscalReportingGatewayRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(response?.Invoke(request) ?? new FiscalReportingGatewayResponse(
                200,
                "application/json",
                Encoding.UTF8.GetBytes("{\"invoices\":[]}"),
                "ok"));
        }
    }

    private sealed class FakeReferenceRepository : IFiscalIssuanceReferenceRepository
    {
        public string? LastIdentifier { get; private set; }
        public Guid? LastSiteId { get; private set; }

        public Task<IReadOnlyList<FiscalIssuanceOperatorLookupMatch>> FindByOperatorIdentifierAsync(
            string identifier,
            Guid? siteId,
            CancellationToken cancellationToken)
        {
            LastIdentifier = identifier;
            LastSiteId = siteId;
            if (identifier == "UNKNOWN")
            {
                return Task.FromResult<IReadOnlyList<FiscalIssuanceOperatorLookupMatch>>([]);
            }

            var match = new FiscalIssuanceOperatorLookupMatch(Reference(), Ticket, Plate);
            return Task.FromResult<IReadOnlyList<FiscalIssuanceOperatorLookupMatch>>(
                identifier == "AMBIGUOUS" ? [match, match] : [match]);
        }

        public Task<FiscalIssuanceReferenceRecord> CreateAsync(CreateFiscalIssuanceReferenceRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FiscalIssuanceReferenceRecord> UpdateStateAsync(Guid fiscalIssuanceReferenceId, FiscalIssuanceStateTransitionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FiscalIssuanceReferenceRecord> RecordSemanticRequestHashAsync(Guid fiscalIssuanceReferenceId, FiscalSemanticRequestHashResult semanticRequestHash, Guid? serviceIdentityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FiscalIssuanceReferenceRecord?> FindByFiscalIssuanceReferenceIdAsync(Guid fiscalIssuanceReferenceId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(Guid paymentConfirmationId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindLatestByPaymentAttemptIdAsync(Guid paymentAttemptId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(string upstreamFinalityReference, Guid? sitePosServerId, Guid? fiscalDocumentTypeCodeId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(Guid posServerFiscalDocumentId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);

        private static FiscalIssuanceReferenceRecord Reference()
        {
            var now = DateTimeOffset.Parse("2026-09-24T10:00:00+08:00");
            return new FiscalIssuanceReferenceRecord(
                FiscalIssuanceReferenceId: Guid.Parse("88000000-0000-0000-0000-000000000010"),
                PaymentConfirmationId: Guid.NewGuid(),
                PaymentAttemptId: Guid.NewGuid(),
                ParkingSessionId: Guid.NewGuid(),
                TariffSnapshotId: Guid.NewGuid(),
                SiteId,
                SitePosServerId: Guid.NewGuid(),
                SitePosServerRef: "PITX-L3-WEBPAY-01",
                PayableBasisRef: "regular",
                UpstreamFinalityReference: "payment-finality",
                PosServerFiscalDocumentId: Guid.NewGuid(),
                FiscalIdentityId: Guid.NewGuid(),
                FiscalSequencePolicyId: Guid.NewGuid(),
                FiscalSequenceValue: 49,
                FiscalDocumentNumber: SalesInvoice,
                FiscalSeries: "SI",
                FiscalNumberPrefixText: "SI-",
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: now,
                FiscalNumberAssignedByRef: "pos-server",
                FiscalDocumentStatusCodeId: Guid.NewGuid(),
                ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                LatestExceptionReason: null,
                LatestErrorCode: null,
                LatestErrorPosture: null,
                CorrelationId: Guid.NewGuid(),
                PosServerResponseTimestamp: now,
                FirstRecordedAt: now,
                LastUpdatedAt: now,
                RecordedByServiceIdentityId: Guid.NewGuid());
        }
    }

    private sealed class FakeRbacRepository(string role) : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(Guid userId, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> UserHasAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken) =>
            Task.FromResult(roleCodes.Contains(role));
        public Task<bool> ServiceIdentityIsActiveAsync(Guid serviceIdentityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task RecordDeniedAsync(string policyName, Guid? userId, Guid? serviceIdentityId, Guid? correlationId, string requestPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task RecordAuditEventAsync(string eventType, string eventResult, string eventReasonCode, string targetEntityType, Guid? targetEntityId, Guid? actorUserId, Guid? actorServiceIdentityId, Guid? correlationId, string summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record CloseableTestPeriod(Guid FiscalReportingPeriodId, string FiscalBusinessDate, long Version);
}
