namespace ExitPass.CentralPms.Application.Operations;

/// <summary>
/// Read-only Central PMS local status lookup for ticket session summaries.
/// </summary>
public interface ITicketSessionSummaryReadRepository
{
    /// <summary>
    /// Finds the local session/payment status for a ticket within the optional Central PMS scope.
    /// </summary>
    Task<TicketSessionLocalStatusResult> FindLocalStatusAsync(
        string ticketNumber,
        Guid? siteId,
        Guid? siteGroupId,
        CancellationToken cancellationToken);
}
