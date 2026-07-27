namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay-facing request to resolve parking, route a provider, and create a payment handoff.
/// </summary>
public sealed class WebPayPaymentIntentRequest
{
    /// <summary>
    /// Optional site group identifier for vendor resolution and provider routing.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Optional site identifier for vendor resolution and provider routing.
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
    /// Normalized ticket reference entered manually or produced by a future QR scan flow.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// Customer-selected payment method code.
    /// </summary>
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// Optional provider override requested by the caller.
    /// </summary>
    public string? PreferredProviderCode { get; set; }

    /// <summary>
    /// Optional final approved tariff snapshot expected by WebPay after coupon or statutory discount validation.
    /// </summary>
    public Guid? TariffSnapshotId { get; set; }

    /// <summary>
    /// Optional final payable amount expected by WebPay after backend-approved payable-basis modifiers.
    /// </summary>
    public long? ExpectedAmountMinorUnits { get; set; }

    /// <summary>
    /// Optional final payable currency expected by WebPay after backend-approved payable-basis modifiers.
    /// </summary>
    public string? ExpectedCurrency { get; set; }

    /// <summary>
    /// Optional canonical statutory-discount decision command that must be payment-ready before payment starts.
    /// </summary>
    public Guid? StatutoryDiscountDecisionCommandId { get; set; }

    /// <summary>
    /// Optional canonical payable-basis application command expected by WebPay after statutory approval.
    /// </summary>
    public Guid? StatutoryDiscountPayableBasisApplicationCommandId { get; set; }

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid? CorrelationId { get; set; }
}
