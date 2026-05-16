namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay-facing response containing provider-neutral payment handoff data.
/// </summary>
public sealed class WebPayPaymentIntentResponse
{
    /// <summary>
    /// Canonical Central PMS payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; set; }

    /// <summary>
    /// Canonical Central PMS parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Canonical Central PMS tariff snapshot identifier.
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
    /// Customer-selected payment method code.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Provider code selected by routing policy.
    /// </summary>
    public string SelectedProviderCode { get; set; } = string.Empty;

    /// <summary>
    /// Optional fallback provider code configured by routing policy.
    /// </summary>
    public string? FallbackProviderCode { get; set; }

    /// <summary>
    /// Deterministic routing reason returned by provider routing.
    /// </summary>
    public string RoutingReason { get; set; } = string.Empty;

    /// <summary>
    /// Current payment attempt or provider-session status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Provider-neutral handoff data for the browser or mobile WebPay client.
    /// </summary>
    public WebPayPaymentHandoffDto Handoff { get; set; } = new();

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
