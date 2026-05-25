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

    /// <summary>
    /// Gets the PayMongo webhook secret key used for webhook signature verification.
    /// </summary>
    public string WebhookSecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the configured webhook runs in live mode.
    /// </summary>
    public bool IsLiveMode { get; init; }

    /// <summary>
    /// Gets the tolerated PayMongo signature timestamp drift in seconds.
    /// </summary>
    public int WebhookSignatureToleranceSeconds { get; init; } = 300;

    /// <summary>
    /// Validates the PayMongo provider options without exposing configured secrets.
    /// </summary>
    /// <returns>The validation errors, or an empty collection when valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            errors.Add("SecretKey is required.");
        }

        if (string.IsNullOrWhiteSpace(PublicKey))
        {
            errors.Add("PublicKey is required.");
        }

        if (string.IsNullOrWhiteSpace(WebhookSecretKey))
        {
            errors.Add("WebhookSecretKey is required.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback))
        {
            errors.Add("BaseUrl must be an absolute HTTPS URL, except for loopback test hosts.");
        }

        if (AllowedPaymentMethodTypes.Any(method => string.IsNullOrWhiteSpace(method)))
        {
            errors.Add("AllowedPaymentMethodTypes must not contain empty entries.");
        }

        if (WebhookSignatureToleranceSeconds is < 60 or > 86_400)
        {
            errors.Add("WebhookSignatureToleranceSeconds must be between 60 and 86400.");
        }

        return errors;
    }
}
