using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Application.Eventing;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

/// <summary>
/// RabbitMQ-backed publisher for Central PMS reconciliation outbox events.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - RabbitMQ publication is a retryable outbox side effect and never mutates payment, provider, exit, gate, or settlement truth.
/// - Database outbox state remains authoritative for dispatch success, retry, and dead-letter evidence.
/// </summary>
public sealed class RabbitMqReconciliationOutboxEventPublisher : IReconciliationOutboxEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqReconciliationOutboxPublisherOptions _options;
    private readonly ILogger<RabbitMqReconciliationOutboxEventPublisher> _logger;

    /// <summary>
    /// Creates the RabbitMQ reconciliation outbox publisher.
    /// </summary>
    public RabbitMqReconciliationOutboxEventPublisher(
        RabbitMqReconciliationOutboxPublisherOptions options,
        ILogger<RabbitMqReconciliationOutboxEventPublisher> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string BrokerType => "RABBITMQ";

    /// <inheritdoc />
    public Task<ReconciliationOutboxPublishOutcome> PublishAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!_options.IsConfigured)
            {
                return Task.FromResult(Failed("RABBITMQ_NOT_CONFIGURED", "RabbitMQ reconciliation outbox publisher is not configured."));
            }

            var exchangeName = ResolveExchangeName(outboxEvent);
            var routingKey = ResolveRoutingKey(outboxEvent);
            var message = ToMessage(outboxEvent);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));

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
                exchange: exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);
            channel.ConfirmSelect();

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = outboxEvent.OutboxEventId.ToString();
            properties.CorrelationId = outboxEvent.CorrelationId?.ToString();
            properties.Type = outboxEvent.EventType;
            properties.Timestamp = new AmqpTimestamp(outboxEvent.CreatedAt.ToUnixTimeSeconds());
            properties.Headers = BuildHeaders(outboxEvent);

            channel.BasicPublish(
                exchange: exchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
            channel.WaitForConfirmsOrDie(_options.PublishConfirmTimeout);

            _logger.LogInformation(
                "Published reconciliation outbox event to RabbitMQ. outbox_event_id={OutboxEventId} event_type={EventType} routing_key={RoutingKey} correlation_id={CorrelationId}",
                outboxEvent.OutboxEventId,
                outboxEvent.EventType,
                routingKey,
                outboxEvent.CorrelationId);

            return Task.FromResult(new ReconciliationOutboxPublishOutcome(
                Succeeded: true,
                BrokerMessageId: outboxEvent.OutboxEventId.ToString(),
                FailureReasonCode: null,
                FailureDetailRef: null));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish reconciliation outbox event to RabbitMQ. outbox_event_id={OutboxEventId} event_type={EventType} correlation_id={CorrelationId}",
                outboxEvent.OutboxEventId,
                outboxEvent.EventType,
                outboxEvent.CorrelationId);

            return Task.FromResult(Failed("RABBITMQ_PUBLISH_FAILED", $"rabbitmq://{_options.Host}:{_options.Port}/{_options.VirtualHost}"));
        }
    }

    /// <summary>
    /// Builds the deterministic message envelope used for RabbitMQ publication.
    /// </summary>
    public static ReconciliationOutboxRabbitMqMessage ToMessage(ReconciliationOutboxEventRecord outboxEvent)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);

        return new ReconciliationOutboxRabbitMqMessage(
            EventId: outboxEvent.OutboxEventId,
            DomainEventId: outboxEvent.DomainEventId,
            EventType: outboxEvent.EventType,
            EventVersion: outboxEvent.EventVersion,
            AggregateType: outboxEvent.AggregateType,
            AggregateId: outboxEvent.AggregateId,
            CorrelationId: outboxEvent.CorrelationId,
            CausationId: outboxEvent.CausationId,
            OccurredAtUtc: outboxEvent.CreatedAt,
            Payload: new ReconciliationOutboxRabbitMqPayload(
                outboxEvent.PayloadRef,
                outboxEvent.PayloadHash,
                outboxEvent.PayloadContentType));
    }

    private string ResolveExchangeName(ReconciliationOutboxEventRecord outboxEvent) =>
        !string.IsNullOrWhiteSpace(outboxEvent.ExchangeName)
            ? outboxEvent.ExchangeName!
            : _options.ExchangeName;

    private string ResolveRoutingKey(ReconciliationOutboxEventRecord outboxEvent)
    {
        if (!string.IsNullOrWhiteSpace(_options.RoutingKeyOverride))
        {
            return _options.RoutingKeyOverride!;
        }

        if (!string.IsNullOrWhiteSpace(outboxEvent.RoutingKey))
        {
            return outboxEvent.RoutingKey;
        }

        return $"{_options.RoutingKeyPrefix}.{outboxEvent.EventType}";
    }

    private static Dictionary<string, object> BuildHeaders(ReconciliationOutboxEventRecord outboxEvent)
    {
        var headers = new Dictionary<string, object>
        {
            ["event_id"] = outboxEvent.OutboxEventId.ToString(),
            ["event_type"] = outboxEvent.EventType,
            ["event_version"] = outboxEvent.EventVersion,
            ["aggregate_type"] = outboxEvent.AggregateType,
            ["aggregate_id"] = outboxEvent.AggregateId.ToString()
        };

        if (outboxEvent.DomainEventId.HasValue)
        {
            headers["domain_event_id"] = outboxEvent.DomainEventId.Value.ToString();
        }

        if (outboxEvent.CorrelationId.HasValue)
        {
            headers["correlation_id"] = outboxEvent.CorrelationId.Value.ToString();
        }

        if (outboxEvent.CausationId.HasValue)
        {
            headers["causation_id"] = outboxEvent.CausationId.Value.ToString();
        }

        return headers;
    }

    private static ReconciliationOutboxPublishOutcome Failed(string reasonCode, string detailRef) =>
        new(
            Succeeded: false,
            BrokerMessageId: null,
            FailureReasonCode: reasonCode,
            FailureDetailRef: detailRef);
}

/// <summary>
/// RabbitMQ message envelope for a reconciliation outbox event.
/// </summary>
public sealed record ReconciliationOutboxRabbitMqMessage(
    Guid EventId,
    Guid? DomainEventId,
    string EventType,
    int EventVersion,
    string AggregateType,
    Guid AggregateId,
    Guid? CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredAtUtc,
    ReconciliationOutboxRabbitMqPayload Payload);

/// <summary>
/// Payload pointer metadata for a reconciliation outbox event.
/// </summary>
public sealed record ReconciliationOutboxRabbitMqPayload(
    string? PayloadRef,
    string? PayloadHash,
    string PayloadContentType);
