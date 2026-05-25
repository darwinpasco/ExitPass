namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Publishes a claimed reconciliation outbox event to the configured broker boundary.
/// </summary>
public interface IReconciliationOutboxEventPublisher
{
    /// <summary>
    /// Publishes one claimed outbox event.
    /// </summary>
    Task<ReconciliationOutboxPublishOutcome> PublishAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        CancellationToken cancellationToken);
}
