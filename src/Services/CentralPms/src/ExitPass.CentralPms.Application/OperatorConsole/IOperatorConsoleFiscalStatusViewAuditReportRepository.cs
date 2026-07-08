namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Reads safe fiscal status view-audit report rows.
/// </summary>
public interface IOperatorConsoleFiscalStatusViewAuditReportRepository
{
    /// <summary>
    /// Lists fiscal status view-audit rows using safe metadata only.
    /// </summary>
    Task<OperatorConsoleFiscalStatusViewAuditReportResult> ListAsync(
        OperatorConsoleFiscalStatusViewAuditReportQuery query,
        CancellationToken cancellationToken);
}
