namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Dispatches durable reconciliation outbox events.
/// </summary>
public interface IReconciliationOutboxDispatcherService
{
    /// <summary>
    /// Dispatches one batch of pending reconciliation outbox events.
    /// </summary>
    Task<ReconciliationOutboxDispatchResult> DispatchOnceAsync(
        DispatchReconciliationOutboxOnceCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists pending reconciliation outbox events.
    /// </summary>
    Task<IReadOnlyList<ReconciliationOutboxPendingRecord>> ListPendingAsync(
        ListPendingReconciliationOutboxQuery query,
        CancellationToken cancellationToken);
}
