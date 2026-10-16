namespace ExitPass.CentralPms.Application.FiscalReporting;

public enum FiscalReportingGatewayAction
{
    ElectronicJournalRead,
    ElectronicJournalDownload,
    XHistory,
    XGenerate,
    XDownload,
    ZHistory,
    ZCloseablePeriods,
    ZClosePeriod,
    ZGenerate,
    ZDownload,
    BirSalesSummaryGenerate,
    BirSalesSummaryRead,
    BirSalesSummaryDownloadJson,
    BirSalesSummaryDownloadCsv,
    AnnexE1Generate,
    AnnexE1CurrentRead,
    AnnexE1Read,
    AnnexE1Download
}

public sealed record FiscalReportingGatewayRequest(
    Guid SiteId,
    FiscalReportingGatewayAction Action,
    Guid CorrelationId,
    string ActorReference,
    DateTimeOffset? PeriodStart = null,
    DateTimeOffset? PeriodEnd = null,
    string? Search = null,
    string? OperationKey = null,
    string? ReportReference = null,
    Guid? FiscalReportingPeriodId = null,
    long? ExpectedStateVersion = null,
    Guid? SummaryId = null,
    Guid? WorkbookId = null,
    string? GoverningZReadingReference = null,
    int? CalendarYear = null,
    int? CalendarMonth = null);

public sealed record FiscalReportingGatewayResponse(
    int HttpStatusCode,
    string ContentType,
    byte[] Body,
    string Code,
    string? FileName = null,
    bool Retryable = false,
    string? ContentSha256 = null);

public interface IFiscalReportingPosServerGateway
{
    Task<FiscalReportingGatewayResponse> SendAsync(FiscalReportingGatewayRequest request, CancellationToken cancellationToken = default);
}
