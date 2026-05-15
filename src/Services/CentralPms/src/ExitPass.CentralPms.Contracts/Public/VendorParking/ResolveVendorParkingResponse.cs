namespace ExitPass.CentralPms.Contracts.Public.VendorParking;

/// <summary>
/// Public API response returned after vendor parking session and tariff resolution succeeds.
/// </summary>
public sealed class ResolveVendorParkingResponse
{
    /// <summary>
    /// Central PMS parking session identifier resolved for the vendor parking session.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Central PMS tariff snapshot identifier resolved for the vendor tariff quote.
    /// </summary>
    public Guid TariffSnapshotId { get; set; }

    /// <summary>
    /// Provider-neutral lookup outcome.
    /// </summary>
    public string LookupOutcome { get; set; } = string.Empty;

    /// <summary>
    /// Vehicle plate number associated with the resolved parking session.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Ticket reference associated with the resolved parking session.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// Net payable amount in minor currency units.
    /// </summary>
    public long NetPayableMinorUnits { get; set; }

    /// <summary>
    /// ISO currency code for the resolved tariff quote.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp after which the tariff snapshot should not be used for payment initiation.
    /// </summary>
    public DateTimeOffset TariffExpiresAt { get; set; }

    /// <summary>
    /// Provider-neutral vendor system identifier used for the lookup.
    /// </summary>
    public string VendorSystemId { get; set; } = string.Empty;

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
