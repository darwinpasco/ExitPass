namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// Provider-neutral handoff instructions for continuing a WebPay payment flow.
/// </summary>
public sealed class WebPayPaymentHandoffDto
{
    /// <summary>
    /// Provider-neutral handoff type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Hosted payment or redirect URL, when available.
    /// </summary>
    public string? HandoffUrl { get; set; }

    /// <summary>
    /// QR payload or image URL, when available.
    /// </summary>
    public string? QrCodeUrl { get; set; }

    /// <summary>
    /// Optional expiration timestamp for the handoff artifact.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
