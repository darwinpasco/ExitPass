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
    /// Gets the persisted gate authorization consumption identifier.
    /// </summary>
    public Guid? GateAuthorizationConsumptionId { get; init; }

    /// <summary>
    /// Gets the parking session tied to the consumed authorization.
    /// </summary>
    public Guid? ParkingSessionId { get; init; }

    /// <summary>
    /// Gets the confirmed payment attempt tied to the consumed authorization.
    /// </summary>
    public Guid? PaymentAttemptId { get; init; }

    /// <summary>
    /// Gets the paid tariff snapshot stored on the confirmed payment attempt.
    /// </summary>
    public Guid? TariffSnapshotId { get; init; }

    /// <summary>
    /// Gets the validated gate device identifier, when available.
    /// </summary>
    public Guid? GateDeviceId { get; init; }

    /// <summary>
    /// Gets the gate device identifier supplied by the caller, when available.
    /// </summary>
    public string? GateDeviceIdentifier { get; init; }

    /// <summary>
    /// Gets the validated lane scope, when available.
    /// </summary>
    public Guid? LaneId { get; init; }

    /// <summary>
    /// Gets the validated site scope, when available.
    /// </summary>
    public Guid? SiteId { get; init; }

    /// <summary>
    /// Gets the vendor PMS system tied to the parking session, when available.
    /// </summary>
    public Guid? VendorSystemId { get; init; }

    /// <summary>
    /// Gets the authorization status after consumption.
    /// </summary>
    public string AuthorizationStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the consumption timestamp.
    /// </summary>
    public DateTimeOffset ConsumedAtUtc { get; init; }

    /// <summary>
    /// Gets the correlation identifier carried by the consume request.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
