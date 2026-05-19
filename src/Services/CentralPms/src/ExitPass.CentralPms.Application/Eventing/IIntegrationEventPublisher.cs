namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Publishes Central PMS integration events after authoritative state changes complete.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes the supplied integration event envelope.
    /// </summary>
    /// <param name="envelope">Event envelope to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when publishing has completed or failed deterministically.</returns>
    Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}
