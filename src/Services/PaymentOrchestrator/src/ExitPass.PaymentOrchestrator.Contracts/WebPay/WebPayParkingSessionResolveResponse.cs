namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay-facing parking session and tariff summary returned before payment attempt creation.
/// </summary>
public sealed class WebPayParkingSessionResolveResponse
{
    /// <summary>
    /// Canonical Central PMS parking session identifier for support traceability.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Canonical Central PMS tariff snapshot identifier for support traceability.
    /// </summary>
    public Guid TariffSnapshotId { get; set; }

    /// <summary>
    /// Payable amount in minor currency units.
    /// </summary>
    public long AmountMinorUnits { get; set; }

    /// <summary>
    /// ISO currency code.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

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
    /// Tariff snapshot expiry or fee validity timestamp, when available.
    /// </summary>
    public DateTimeOffset? FeeValidUntil { get; set; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
