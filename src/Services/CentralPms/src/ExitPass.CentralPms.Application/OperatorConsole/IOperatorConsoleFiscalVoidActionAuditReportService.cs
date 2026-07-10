namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only service for fiscal void action audit review.
/// </summary>
public interface IOperatorConsoleFiscalVoidActionAuditReportService
{
    /// <summary>
    /// Lists fiscal void action audit review rows.
    /// </summary>
    Task<OperatorConsoleFiscalVoidActionAuditReportResult> ListAsync(
        OperatorConsoleFiscalVoidActionAuditReportQuery query,
        CancellationToken cancellationToken);
}
