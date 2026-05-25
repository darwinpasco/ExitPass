namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Publishes a claimed reconciliation outbox event to the configured broker boundary.
/// </summary>
public interface IReconciliationOutboxEventPublisher
{
    /// <summary>
    /// Broker type stored in events.event_publications for attempts made by this publisher.
    /// </summary>
    string BrokerType { get; }

    /// <summary>
    /// Publishes one claimed outbox event.
    /// </summary>
    Task<ReconciliationOutboxPublishOutcome> PublishAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        CancellationToken cancellationToken);
}
