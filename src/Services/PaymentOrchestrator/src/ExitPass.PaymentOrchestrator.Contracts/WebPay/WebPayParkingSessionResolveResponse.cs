namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay-facing parking session and tariff summary returned before payment attempt creation.
/// </summary>
public sealed class WebPayParkingSessionResolveResponse
{
    /// <summary>
    /// Canonical Central PMS parking session identifier for support traceability.
    /// </summary>
    public Guid? ParkingSessionId { get; set; }

    /// <summary>
    /// Canonical Central PMS tariff snapshot identifier for support traceability.
    /// </summary>
    public Guid? TariffSnapshotId { get; set; }

    /// <summary>Whether a safe live or projected session identity was found.</summary>
    public bool SessionFound { get; set; }

    /// <summary>Live vendor or continuity-projection source.</summary>
    public string SessionSource { get; set; } = string.Empty;

    /// <summary>Whether this is a session-only degraded response.</summary>
    public bool Degraded { get; set; }

    /// <summary>Whether authoritative tariff and payable-basis facts are present.</summary>
    public bool PayableBasisAvailable { get; set; }

    /// <summary>
    /// Site group resolved with the parking session.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Site resolved with the parking session.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Provider-neutral vendor system identifier resolved with the parking session.
    /// </summary>
    public string? VendorSystemId { get; set; }

    /// <summary>
    /// Business-friendly site group name, when available.
    /// </summary>
    public string? SiteGroupName { get; set; }

    /// <summary>
    /// Payable amount in minor currency units.
    /// </summary>
    public long? AmountMinorUnits { get; set; }

    /// <summary>
    /// Indicates whether customer information has already been provided for the Sales Invoice.
    /// Customer-entered values are intentionally not returned by this public contract.
    /// </summary>
    public bool? CustomerInformationSubmitted { get; set; }

    /// <summary>
    /// ISO currency code.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Business-friendly site or parking location name, when supplied by the resolved context.
    /// </summary>
    public string? SiteName { get; set; }

    /// <summary>
    /// Parker-facing ticket reference, when available.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// Parker-facing plate number, when available.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Parking entry timestamp, when available.
    /// </summary>
    public DateTimeOffset? EntryTime { get; set; }

    /// <summary>
    /// Fee calculation timestamp, when available.
    /// </summary>
    public DateTimeOffset? CurrentFeeCalculationTime { get; set; }

    /// <summary>
    /// Human-readable tariff or rate name, when available.
    /// </summary>
    public string? TariffName { get; set; }

    /// <summary>
    /// Parking session status, when available.
    /// </summary>
    public string? ParkingStatus { get; set; }

    /// <summary>
    /// Current payment attempt or confirmation status for WebPay display.
    /// </summary>
    public string? PaymentStatus { get; set; }

    /// <summary>
    /// Tariff snapshot expiry or fee validity timestamp, when available.
    /// </summary>
    public DateTimeOffset? FeeValidUntil { get; set; }

    /// <summary>Projection identifier used for continuity lookup.</summary>
    public Guid? VendorSessionProjectionId { get; set; }

    /// <summary>Projection lifecycle status.</summary>
    public string? ProjectionStatus { get; set; }

    /// <summary>Successful projection refresh timestamp used for freshness enforcement.</summary>
    public DateTimeOffset? ProjectionLastRefreshedAt { get; set; }

    /// <summary>Age of the projection refresh in seconds.</summary>
    public double? ProjectionFreshnessAgeSeconds { get; set; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
