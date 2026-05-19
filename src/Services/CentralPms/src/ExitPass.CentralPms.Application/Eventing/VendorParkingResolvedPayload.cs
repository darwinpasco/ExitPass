namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the VendorParkingResolved integration event.
/// </summary>
public sealed class VendorParkingResolvedPayload
{
    /// <summary>
    /// Canonical parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Canonical tariff snapshot identifier.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Site identifier supplied by the caller.
    /// </summary>
    public string SiteId { get; init; } = string.Empty;

    /// <summary>
    /// Site group identifier supplied by the caller.
    /// </summary>
    public string SiteGroupId { get; init; } = string.Empty;

    /// <summary>
    /// Vendor PMS system identifier.
    /// </summary>
    public string VendorSystemId { get; init; } = string.Empty;

    /// <summary>
    /// Lookup reference type used by the caller, such as plate or ticket.
    /// </summary>
    public string LookupReferenceType { get; init; } = string.Empty;

    /// <summary>
    /// Bounded lookup outcome.
    /// </summary>
    public string LookupOutcome { get; init; } = string.Empty;

    /// <summary>
    /// Net payable amount in minor currency units.
    /// </summary>
    public long NetPayableMinorUnits { get; init; }

    /// <summary>
    /// Currency code for the payable amount.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when the resolved tariff expires.
    /// </summary>
    public DateTimeOffset TariffExpiresAt { get; init; }
}
