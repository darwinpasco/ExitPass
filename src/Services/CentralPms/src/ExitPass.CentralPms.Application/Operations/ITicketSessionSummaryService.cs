namespace ExitPass.CentralPms.Application.Operations;

/// <summary>
/// Read-only service for ops-facing ticket session summaries.
/// </summary>
public interface ITicketSessionSummaryService
{
    /// <summary>
    /// Retrieves and composes vendor ticket/session/tariff and local payment status summary.
    /// </summary>
    Task<TicketSessionSummaryResult> GetAsync(
        TicketSessionSummaryCommand command,
        CancellationToken cancellationToken);
}
