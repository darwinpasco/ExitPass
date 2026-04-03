namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// Configuration settings for the PayMongo provider integration.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 4.2.7 Payment Orchestrator
/// - 11 Security Architecture
///
/// Invariants Enforced:
/// - Provider credentials and transport settings must be externalized from code.
/// </summary>
public sealed class PayMongoOptions
{
    /// <summary>
    /// The configuration section name for PayMongo settings.
    /// </summary>
    public const string SectionName = "Payments:Providers:PayMongo";

    /// <summary>
    /// Gets or initializes the PayMongo secret API key.
    /// </summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the PayMongo public API key.
    /// </summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the PayMongo base API URL.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.paymongo.com";

    /// <summary>
    /// Gets or initializes the allowed payment method types for Checkout Session creation.
    /// </summary>
    public string[] AllowedPaymentMethodTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets or initializes the webhook signing secret or equivalent verification material.
    /// </summary>
    public string WebhookSigningSecret { get; init; } = string.Empty;
}
