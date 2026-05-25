namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Repository boundary for durable reconciliation outbox dispatch state transitions.
/// </summary>
public interface IReconciliationOutboxDispatcherRepository
{
    /// <summary>
    /// Lists pending reconciliation outbox events without mutating state.
    /// </summary>
    Task<IReadOnlyList<ReconciliationOutboxPendingRecord>> ListPendingAsync(
        ListPendingReconciliationOutboxQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims pending reconciliation outbox events and records publication attempts in one database transaction.
    /// </summary>
    Task<IReadOnlyList<ReconciliationOutboxEventRecord>> ClaimPendingAsync(
        DispatchReconciliationOutboxOnceCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed outbox event and its publication attempt as published.
    /// </summary>
    Task MarkPublishedAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        ReconciliationOutboxPublishOutcome outcome,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed outbox event and its publication attempt as retry-pending or dead-lettered.
    /// </summary>
    Task<ReconciliationOutboxDispatchItemResult> MarkFailedAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        ReconciliationOutboxPublishOutcome outcome,
        CancellationToken cancellationToken);
}
