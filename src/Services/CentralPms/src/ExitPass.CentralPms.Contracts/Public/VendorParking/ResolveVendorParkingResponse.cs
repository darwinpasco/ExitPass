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
    /// Site group that owns the resolved parking session.
    /// </summary>
    public string SiteGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Site that owns the resolved parking session.
    /// </summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>
    /// Business-friendly site group name, when available.
    /// </summary>
    public string? SiteGroupName { get; set; }

    /// <summary>
    /// Business-friendly site name, when available.
    /// </summary>
    public string? SiteName { get; set; }

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
    /// Parking entry timestamp from the resolved parking session.
    /// </summary>
    public DateTimeOffset? EntryTime { get; set; }

    /// <summary>
    /// Timestamp used for the current tariff calculation.
    /// </summary>
    public DateTimeOffset? CurrentFeeCalculationTime { get; set; }

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
    /// Tariff snapshot expiry used by WebPay as the fee-valid-until boundary.
    /// </summary>
    public DateTimeOffset FeeValidUntil { get; set; }

    /// <summary>
    /// Current parking session status.
    /// </summary>
    public string ParkingStatus { get; set; } = string.Empty;

    /// <summary>
    /// Current payment attempt or confirmation status for WebPay display.
    /// </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>
    /// Provider-neutral vendor system identifier used for the lookup.
    /// </summary>
    public string VendorSystemId { get; set; } = string.Empty;

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
