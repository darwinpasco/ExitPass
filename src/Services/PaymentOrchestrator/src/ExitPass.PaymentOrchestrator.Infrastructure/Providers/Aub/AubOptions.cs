namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Configuration settings for the AUB provider integration.
/// </summary>
public sealed class AubOptions
{
    /// <summary>
    /// Gets the configuration section name for AUB settings.
    /// </summary>
    public const string SectionName = "Payments:Providers:Aub";

    /// <summary>
    /// Gets or initializes the AUB base API URL.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional merchant identifier supplied through external configuration.
    /// </summary>
    public string MerchantId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the HTTP request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
}
