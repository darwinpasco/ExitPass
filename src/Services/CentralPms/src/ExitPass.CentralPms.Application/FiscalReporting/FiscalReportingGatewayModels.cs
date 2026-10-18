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
    BirSalesSummaryDownloadPdf,
    AnnexE1KnownZeroAttest,
    AnnexE1Generate,
    AnnexE1CurrentRead,
    AnnexE1Read,
    AnnexE1Download,
    AnnexE1DownloadPdf,
    MonthlyESalesRead,
    MonthlyESalesDownloadJson,
    MonthlyESalesDownloadCsv,
    MonthlyESalesDownloadPdf,
    AuditTrailRead,
    AuditTrailDownload
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
    int? CalendarMonth = null,
    string? ExportFormat = null,
    string? ActionCode = null,
    string? ActorFilter = null,
    string? ResultFilter = null,
    string? ReferenceFilter = null,
    string? FactType = null,
    string? FactStatus = null,
    long? AmountMinorUnits = null,
    long? SourceDocumentCount = null,
    string? FirstSourceReference = null,
    string? LastSourceReference = null,
    string? SourceEventReference = null,
    string? ApprovalReference = null,
    Guid? SupersedesFactId = null,
    string? CorrectionReason = null,
    Guid? SupersedesWorkbookId = null,
    string? CorrectionApprovalReference = null);

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
