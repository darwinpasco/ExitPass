namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only service for fiscal status view-audit reporting.
/// </summary>
public interface IOperatorConsoleFiscalStatusViewAuditReportService
{
    /// <summary>
    /// Lists fiscal status view-audit report rows.
    /// </summary>
    Task<OperatorConsoleFiscalStatusViewAuditReportResult> ListAsync(
        OperatorConsoleFiscalStatusViewAuditReportQuery query,
        CancellationToken cancellationToken);
}
