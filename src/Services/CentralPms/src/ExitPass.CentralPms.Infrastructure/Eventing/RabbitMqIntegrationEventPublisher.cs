using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.Eventing;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

internal sealed class RabbitMqIntegrationEventPublisher : IIntegrationEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqIntegrationEventPublisherOptions _options;
    private readonly ILogger<RabbitMqIntegrationEventPublisher> _logger;

    public RabbitMqIntegrationEventPublisher(
        RabbitMqIntegrationEventPublisherOptions options,
        ILogger<RabbitMqIntegrationEventPublisher> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.ExchangeDeclare(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);
            channel.ConfirmSelect();

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOptions));
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = envelope.EventId.ToString();
            properties.CorrelationId = envelope.CorrelationId.ToString();
            properties.Type = envelope.EventType;
            properties.Timestamp = new AmqpTimestamp(envelope.OccurredAtUtc.ToUnixTimeSeconds());

            channel.BasicPublish(
                exchange: _options.ExchangeName,
                routingKey: RoutingKeyFor(envelope.EventType),
                mandatory: false,
                basicProperties: properties,
                body: body);
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

            _logger.LogInformation(
                "Published Central PMS integration event. event_type={EventType} event_id={EventId} correlation_id={CorrelationId}",
                envelope.EventType,
                envelope.EventId,
                envelope.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish Central PMS integration event. event_type={EventType} event_id={EventId} correlation_id={CorrelationId}",
                envelope.EventType,
                envelope.EventId,
                envelope.CorrelationId);

            throw;
        }

        return Task.CompletedTask;
    }

    private static string RoutingKeyFor(string eventType)
    {
        return $"central-pms.{eventType}";
    }
}
