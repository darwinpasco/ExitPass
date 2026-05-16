namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Versioned metadata envelope for Central PMS integration events.
/// </summary>
public sealed class IntegrationEventEnvelope
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Stable event contract type name.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>
    /// End-to-end correlation identifier preserved from the triggering request.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Optional identifier of the command or event that caused this event.
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>
    /// Identifier of the aggregate whose authoritative state changed.
    /// </summary>
    public string AggregateId { get; init; } = string.Empty;

    /// <summary>
    /// Type name of the aggregate whose authoritative state changed.
    /// </summary>
    public string AggregateType { get; init; } = string.Empty;

    /// <summary>
    /// Schema version for the event envelope and payload contract.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Event-specific payload.
    /// </summary>
    public object Payload { get; init; } = new();
}
