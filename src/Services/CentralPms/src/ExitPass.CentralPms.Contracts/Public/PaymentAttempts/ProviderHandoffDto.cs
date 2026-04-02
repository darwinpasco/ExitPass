namespace ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

/// <summary>
/// Provider handoff information needed by the client to continue the external payment flow.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
///
/// Invariants Enforced:
/// - Handoff metadata is carried separately from the canonical payment attempt state
/// - Redirect or hosted-payment continuation data is explicit and structured
/// </summary>
public sealed class ProviderHandoffDto
{
    /// <summary>
    /// Handoff type, such as redirect, deep_link, or qr_payload.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Optional provider URL or redirect target for the next step in the payment journey.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Optional expiration timestamp after which the handoff artifact should no longer be used.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
