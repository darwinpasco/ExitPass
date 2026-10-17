using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;

namespace ExitPass.CentralPms.Infrastructure.FiscalReporting;

public sealed class HttpFiscalReportingPosServerGateway(
    HttpClient httpClient,
    ISitePosServerBindingResolver bindingResolver) : IFiscalReportingPosServerGateway
{
    private const string AnnexE1Profile = "pos-server-bir-annex-e1-rmo24-2023:v1";
    private const string ApiKeyHeader = "X-PosServer-Admin-Key";
    private const string PermissionHeader = "X-PosServer-Admin-Permission";
    public async Task<FiscalReportingGatewayResponse> SendAsync(FiscalReportingGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var resolution = Resolve(request.SiteId);
        if (resolution.Error is not null) return Failure(503, resolution.Error, true);
        try
        {
            if (request.Action == FiscalReportingGatewayAction.ZGenerate)
                return await GenerateZAsync(resolution.Endpoint!, request, cancellationToken).ConfigureAwait(false);

            using var outbound = BuildRequest(resolution.Endpoint!, request);
            using var response = await httpClient.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            var contentSha256 = response.Headers.TryGetValues("X-Content-SHA256", out var hashes)
                ? hashes.SingleOrDefault()
                : response.Headers.ETag?.Tag?.Trim('"').Replace("sha256-", string.Empty, StringComparison.OrdinalIgnoreCase);
            return new((int)response.StatusCode, contentType, bytes, response.IsSuccessStatusCode ? "pos_fiscal_reporting_succeeded" : "pos_fiscal_reporting_failed", fileName, (int)response.StatusCode >= 500, contentSha256);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Failure(503, "site_pos_server_unavailable", true);
        }
    }

    private async Task<FiscalReportingGatewayResponse> GenerateZAsync(ResolvedEndpoint endpoint, FiscalReportingGatewayRequest request, CancellationToken ct)
    {
        using var historyRequest = Build(endpoint, HttpMethod.Get, $"/v1/fiscal-reports/z-readings/history?{Scope(endpoint)}", "fiscal_z_reading.read", request.CorrelationId);
        using var historyResponse = await httpClient.SendAsync(historyRequest, ct).ConfigureAwait(false);
        var historyBytes = await historyResponse.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (!historyResponse.IsSuccessStatusCode) return new((int)historyResponse.StatusCode, "application/json; charset=utf-8", historyBytes, "fiscal_reporting_period_unavailable", Retryable: (int)historyResponse.StatusCode >= 500);
        JsonDocument history;
        try
        {
            history = JsonDocument.Parse(historyBytes);
        }
        catch (JsonException)
        {
            return Failure(502, "fiscal_reporting_upstream_response_invalid", true);
        }
        using (history)
        {
        if (!history.RootElement.TryGetProperty("currentPeriod", out var period) || period.ValueKind != JsonValueKind.Object ||
            !period.TryGetProperty("fiscalReportingPeriodId", out var periodId) || !period.TryGetProperty("expectedZStateVersion", out var stateVersion) ||
            periodId.ValueKind != JsonValueKind.String || stateVersion.ValueKind != JsonValueKind.Number)
            return Failure(409, "fiscal_reporting_open_period_unavailable", false);

        using var outbound = Build(endpoint, HttpMethod.Post, "/v1/fiscal-reports/z-readings", "fiscal_z_reading.close", request.CorrelationId);
        outbound.Content = JsonContent.Create(new
        {
            operationKey = request.OperationKey,
            sitePosServerId = endpoint.SitePosServerId,
            fiscalIdentityId = endpoint.FiscalIdentityId,
            currencyCode = endpoint.CurrencyCode,
            fiscalReportingPeriodId = periodId.GetString(),
            expectedStateVersion = stateVersion.GetInt64()
        });
        using var response = await httpClient.SendAsync(outbound, ct).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return new((int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8", bytes,
            response.IsSuccessStatusCode ? "pos_fiscal_reporting_succeeded" : "pos_fiscal_reporting_failed", Retryable: (int)response.StatusCode >= 500);
        }
    }

    private HttpRequestMessage BuildRequest(ResolvedEndpoint endpoint, FiscalReportingGatewayRequest request)
    {
        var scope = Scope(endpoint);
        var (method, path, permission, body) = request.Action switch
        {
            FiscalReportingGatewayAction.ElectronicJournalRead => (HttpMethod.Get, $"/v1/electronic-journal/invoice-text?{scope}{Period(request)}{Search(request)}", "electronic_journal.read", (object?)null),
            FiscalReportingGatewayAction.ElectronicJournalDownload => (HttpMethod.Get, $"/v1/electronic-journal/invoice-text.txt?{scope}{Period(request)}{Search(request)}", "electronic_journal.export", null),
            FiscalReportingGatewayAction.XHistory => (HttpMethod.Get, $"/v1/fiscal-reports/x-readings/history?{scope}", "fiscal_x_reading.read", null),
            FiscalReportingGatewayAction.XGenerate => (HttpMethod.Post, "/v1/fiscal-reports/x-readings", "fiscal_x_reading.generate", new { operationKey=request.OperationKey, sitePosServerId=endpoint.SitePosServerId, fiscalIdentityId=endpoint.FiscalIdentityId, observedAt=DateTimeOffset.UtcNow }),
            FiscalReportingGatewayAction.XDownload => (HttpMethod.Get, $"/v1/fiscal-reports/x-readings/{Uri.EscapeDataString(RequireReference(request))}/exports/{RequireReadingExportFormat(request)}", "fiscal_x_reading.export", null),
            FiscalReportingGatewayAction.ZHistory => (HttpMethod.Get, $"/v1/fiscal-reports/z-readings/history?{scope}", "fiscal_z_reading.read", null),
            FiscalReportingGatewayAction.ZCloseablePeriods => (HttpMethod.Get, $"/v1/fiscal-reports/z-readings/closeable-periods?{scope}", "fiscal_z_reading.read", null),
            FiscalReportingGatewayAction.ZClosePeriod => (HttpMethod.Post, "/v1/fiscal-reports/z-readings", "fiscal_z_reading.close", new
            {
                operationKey = request.OperationKey,
                sitePosServerId = endpoint.SitePosServerId,
                fiscalIdentityId = endpoint.FiscalIdentityId,
                currencyCode = endpoint.CurrencyCode,
                fiscalReportingPeriodId = RequirePeriodId(request),
                expectedStateVersion = RequireExpectedStateVersion(request)
            }),
            FiscalReportingGatewayAction.ZDownload => (HttpMethod.Get, $"/v1/fiscal-reports/z-readings/{Uri.EscapeDataString(RequireReference(request))}/exports/{RequireReadingExportFormat(request)}", "fiscal_z_reading.export", null),
            FiscalReportingGatewayAction.BirSalesSummaryGenerate => (HttpMethod.Post, "/v1/fiscal-reports/bir-sales-summaries/", "bir_sales_summary.generate", new
            {
                operationKey = request.OperationKey,
                sitePosServerId = endpoint.SitePosServerId,
                fiscalIdentityId = endpoint.FiscalIdentityId,
                currencyCode = endpoint.CurrencyCode,
                fiscalReportingPeriodId = RequirePeriodId(request),
                governingZReadingReference = RequireGoverningZReference(request)
            }),
            FiscalReportingGatewayAction.BirSalesSummaryRead => (HttpMethod.Get, $"/v1/fiscal-reports/bir-sales-summaries/{RequireGuid(request.SummaryId, "summary"):D}", "bir_sales_summary.read", null),
            FiscalReportingGatewayAction.BirSalesSummaryDownloadJson => (HttpMethod.Get, $"/v1/fiscal-reports/bir-sales-summaries/{RequireGuid(request.SummaryId, "summary"):D}/exports/json", "bir_sales_summary.export", null),
            FiscalReportingGatewayAction.BirSalesSummaryDownloadCsv => (HttpMethod.Get, $"/v1/fiscal-reports/bir-sales-summaries/{RequireGuid(request.SummaryId, "summary"):D}/exports/csv", "bir_sales_summary.export", null),
            FiscalReportingGatewayAction.BirSalesSummaryDownloadPdf => (HttpMethod.Get, $"/v1/fiscal-reports/bir-sales-summaries/{RequireGuid(request.SummaryId, "summary"):D}/exports/pdf", "bir_sales_summary.export", null),
            FiscalReportingGatewayAction.AnnexE1Generate => (HttpMethod.Post, "/v1/fiscal-reports/annex-e/e1/workbooks", "fiscal_annex_e.generate", new
            {
                operationKey = request.OperationKey,
                sitePosServerId = endpoint.SitePosServerId,
                fiscalIdentityId = endpoint.FiscalIdentityId,
                currencyCode = endpoint.CurrencyCode,
                calendarYear = RequireCalendarPart(request.CalendarYear, 2000, 9999, "year"),
                calendarMonth = RequireCalendarPart(request.CalendarMonth, 1, 12, "month"),
                profile = AnnexE1Profile
            }),
            FiscalReportingGatewayAction.AnnexE1CurrentRead => (HttpMethod.Get,
                $"/v1/fiscal-reports/annex-e/e1/workbooks/current?{scope}&calendarYear={RequireCalendarPart(request.CalendarYear, 2000, 9999, "year")}&calendarMonth={RequireCalendarPart(request.CalendarMonth, 1, 12, "month")}&profile={Uri.EscapeDataString(AnnexE1Profile)}",
                "fiscal_annex_e.read", null),
            FiscalReportingGatewayAction.AnnexE1Read => (HttpMethod.Get, $"/v1/fiscal-reports/annex-e/e1/workbooks/{RequireGuid(request.WorkbookId, "workbook"):D}", "fiscal_annex_e.read", null),
            FiscalReportingGatewayAction.AnnexE1Download => (HttpMethod.Get, $"/v1/fiscal-reports/annex-e/e1/workbooks/{RequireGuid(request.WorkbookId, "workbook"):D}/content", "fiscal_annex_e.export", null),
            FiscalReportingGatewayAction.AnnexE1DownloadPdf => (HttpMethod.Get, $"/v1/fiscal-reports/annex-e/e1/workbooks/{RequireGuid(request.WorkbookId, "workbook"):D}/content?format=pdf", "fiscal_annex_e.export", null),
            FiscalReportingGatewayAction.MonthlyESalesRead => (HttpMethod.Get,
                $"/v1/fiscal-reports/monthly-esales/?{scope}&calendarYear={RequireCalendarPart(request.CalendarYear, 2000, 9999, "year")}&calendarMonth={RequireCalendarPart(request.CalendarMonth, 1, 12, "month")}",
                "monthly_esales.read", null),
            FiscalReportingGatewayAction.MonthlyESalesDownloadJson => (HttpMethod.Get,
                $"/v1/fiscal-reports/monthly-esales/download?{scope}&calendarYear={RequireCalendarPart(request.CalendarYear, 2000, 9999, "year")}&calendarMonth={RequireCalendarPart(request.CalendarMonth, 1, 12, "month")}&format=json",
                "monthly_esales.export", null),
            FiscalReportingGatewayAction.MonthlyESalesDownloadCsv => (HttpMethod.Get,
                $"/v1/fiscal-reports/monthly-esales/download?{scope}&calendarYear={RequireCalendarPart(request.CalendarYear, 2000, 9999, "year")}&calendarMonth={RequireCalendarPart(request.CalendarMonth, 1, 12, "month")}&format=csv",
                "monthly_esales.export", null),
            FiscalReportingGatewayAction.MonthlyESalesDownloadPdf => (HttpMethod.Get,
                $"/v1/fiscal-reports/monthly-esales/download?{scope}&calendarYear={RequireCalendarPart(request.CalendarYear, 2000, 9999, "year")}&calendarMonth={RequireCalendarPart(request.CalendarMonth, 1, 12, "month")}&format=pdf",
                "monthly_esales.export", null),
            _ => throw new InvalidOperationException("Unsupported fiscal reporting gateway action.")
        };
        var outbound=Build(endpoint,method,path,permission,request.CorrelationId);
        if(body is not null) outbound.Content=JsonContent.Create(body);
        return outbound;
    }

    private HttpRequestMessage Build(ResolvedEndpoint endpoint, HttpMethod method, string path, string permission, Guid correlation)
    {
        var request=new HttpRequestMessage(method,new Uri(endpoint.BaseUri,path));
        request.Headers.TryAddWithoutValidation(ApiKeyHeader,endpoint.ApiKey);
        request.Headers.TryAddWithoutValidation(PermissionHeader,permission);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id",correlation.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Actor-Reference","central-pms-fiscal-reporting");
        return request;
    }

    private (ResolvedEndpoint? Endpoint,string? Error) Resolve(Guid siteId)
    {
        var binding=bindingResolver.Resolve(new SitePosServerBindingRequest(siteId));
        if(!binding.IsSuccess || binding.Endpoint is null) return (null,binding.Code);
        var value=binding.Endpoint;
        if(value.SitePosServerId==Guid.Empty || !value.FiscalIdentityId.HasValue || value.FiscalIdentityId.Value==Guid.Empty || string.IsNullOrWhiteSpace(value.CurrencyCode)) return (null,"site_pos_server_fiscal_reporting_scope_unavailable");
        if(!Uri.TryCreate(value.BaseUrl,UriKind.Absolute,out var baseUri) || baseUri.AbsolutePath!="/" || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment)) return (null,"site_pos_server_endpoint_url_invalid");
        if(string.IsNullOrWhiteSpace(value.ApiKeyFile) || !File.Exists(value.ApiKeyFile)) return (null,"site_pos_server_api_key_file_unavailable");
        var key=File.ReadAllText(value.ApiKeyFile).Trim(); if(string.IsNullOrWhiteSpace(key)) return (null,"site_pos_server_api_key_file_empty");
        return (new(baseUri,value.SitePosServerId,value.FiscalIdentityId.Value,value.CurrencyCode.Trim().ToUpperInvariant(),key),null);
    }

    private static string Scope(ResolvedEndpoint e)=>$"sitePosServerId={e.SitePosServerId:D}&fiscalIdentityId={e.FiscalIdentityId:D}&currencyCode={e.CurrencyCode}";
    private static string Period(FiscalReportingGatewayRequest r)=>$"&effectiveFrom={Uri.EscapeDataString(r.PeriodStart?.ToUniversalTime().ToString("O")??string.Empty)}&effectiveTo={Uri.EscapeDataString(r.PeriodEnd?.ToUniversalTime().ToString("O")??string.Empty)}";
    private static string Search(FiscalReportingGatewayRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Search)) return string.Empty;
        var value=r.Search.Trim();
        if (Guid.TryParse(value,out var documentId)&&documentId!=Guid.Empty)
            return $"&fiscalDocumentReference={Uri.EscapeDataString(documentId.ToString("D"))}";
        const string correlationPrefix="correlation:";
        if (value.StartsWith(correlationPrefix,StringComparison.OrdinalIgnoreCase))
            return $"&correlationReference={Uri.EscapeDataString(value[correlationPrefix.Length..].Trim())}";
        return $"&fiscalDocumentNumber={Uri.EscapeDataString(value)}";
    }
    private static string RequireReference(FiscalReportingGatewayRequest r)=>!string.IsNullOrWhiteSpace(r.ReportReference)?r.ReportReference.Trim():throw new ArgumentException("Report reference is required.");
    private static string RequireReadingExportFormat(FiscalReportingGatewayRequest r)
    {
        var format=string.IsNullOrWhiteSpace(r.ExportFormat)?"pdf":r.ExportFormat.Trim().ToLowerInvariant();
        return format is "pdf" or "json"?format:throw new ArgumentException("The fiscal report export format is unsupported.");
    }
    private static Guid RequirePeriodId(FiscalReportingGatewayRequest r)=>r.FiscalReportingPeriodId is { } value&&value!=Guid.Empty?value:throw new ArgumentException("Fiscal reporting period is required.");
    private static long RequireExpectedStateVersion(FiscalReportingGatewayRequest r)=>r.ExpectedStateVersion is >0? r.ExpectedStateVersion.Value:throw new ArgumentException("Expected fiscal-period state version is required.");
    private static Guid RequireGuid(Guid? value,string name)=>value is { } id&&id!=Guid.Empty?id:throw new ArgumentException($"A valid {name} identifier is required.");
    private static string RequireGoverningZReference(FiscalReportingGatewayRequest r)=>!string.IsNullOrWhiteSpace(r.GoverningZReadingReference)?r.GoverningZReadingReference.Trim():throw new ArgumentException("The governing Z Reading reference is required.");
    private static int RequireCalendarPart(int? value,int minimum,int maximum,string name)=>value.HasValue&&value.Value>=minimum&&value.Value<=maximum?value.Value:throw new ArgumentException($"A valid calendar {name} is required.");
    private static FiscalReportingGatewayResponse Failure(int status,string code,bool retryable)=>new(status,"application/json; charset=utf-8",Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{succeeded=false,code,message="The authoritative fiscal reporting operation could not be completed."})),code,Retryable:retryable);
    private sealed record ResolvedEndpoint(Uri BaseUri,Guid SitePosServerId,Guid FiscalIdentityId,string CurrencyCode,string ApiKey);
}
