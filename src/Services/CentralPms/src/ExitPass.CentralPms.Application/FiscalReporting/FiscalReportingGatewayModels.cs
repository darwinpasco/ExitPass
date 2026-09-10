namespace ExitPass.CentralPms.Application.FiscalReporting;

public enum FiscalReportingGatewayAction
{
    ElectronicJournalRead,
    ElectronicJournalDownload,
    XHistory,
    XGenerate,
    XDownload,
    ZHistory,
    ZGenerate,
    ZDownload
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
    string? ReportReference = null);

public sealed record FiscalReportingGatewayResponse(
    int HttpStatusCode,
    string ContentType,
    byte[] Body,
    string Code,
    string? FileName = null,
    bool Retryable = false);

public interface IFiscalReportingPosServerGateway
{
    Task<FiscalReportingGatewayResponse> SendAsync(FiscalReportingGatewayRequest request, CancellationToken cancellationToken = default);
}
