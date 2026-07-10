namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only repository for fiscal void action audit review rows.
/// </summary>
public interface IOperatorConsoleFiscalVoidActionAuditReportRepository
{
    /// <summary>
    /// Lists safe fiscal void action audit rows.
    /// </summary>
    Task<OperatorConsoleFiscalVoidActionAuditReportResult> ListAsync(
        OperatorConsoleFiscalVoidActionAuditReportQuery query,
        CancellationToken cancellationToken);
}
