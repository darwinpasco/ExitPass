using ExitPass.CentralPms.Application.Eventing;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

internal sealed class DisabledIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly ILogger<DisabledIntegrationEventPublisher> _logger;

    public DisabledIntegrationEventPublisher(ILogger<DisabledIntegrationEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _logger.LogInformation(
            "Central PMS integration event publishing is disabled because Messaging:RabbitMq:Host is not configured. event_type={EventType} event_id={EventId} correlation_id={CorrelationId}",
            envelope.EventType,
            envelope.EventId,
            envelope.CorrelationId);

        return Task.CompletedTask;
    }
}
