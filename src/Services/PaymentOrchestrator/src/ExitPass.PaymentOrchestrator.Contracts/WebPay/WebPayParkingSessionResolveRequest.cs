namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay-facing request to resolve parking and tariff data before payment attempt creation.
/// </summary>
public sealed class WebPayParkingSessionResolveRequest
{
    /// <summary>
    /// Optional site group identifier for vendor resolution.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Optional site identifier for vendor resolution.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Provider-neutral vendor system identifier for the parking lookup.
    /// </summary>
    public string? VendorSystemId { get; set; }

    /// <summary>
    /// Plate number entered by the parker.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Normalized ticket reference entered manually or from QR.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid? CorrelationId { get; set; }
}
