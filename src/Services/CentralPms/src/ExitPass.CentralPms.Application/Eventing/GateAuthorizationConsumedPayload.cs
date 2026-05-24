namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the GateAuthorizationConsumed integration event.
/// </summary>
public sealed class GateAuthorizationConsumedPayload
{
    /// <summary>
    /// Gets the consumed exit authorization identifier.
    /// </summary>
    public Guid ExitAuthorizationId { get; init; }

    /// <summary>
    /// Gets the authorization status after consumption.
    /// </summary>
    public string AuthorizationStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the consumption timestamp.
    /// </summary>
    public DateTimeOffset ConsumedAtUtc { get; init; }
}
