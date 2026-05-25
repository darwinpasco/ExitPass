namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Runtime configuration for the HikCentral Professional Vendor PMS adapter.
///
/// BRD v1.2 Reference:
/// - Section 9.8 Parking Session Lookup
/// - Section 9.9 Tariff and Fee Calculation
///
/// SDD v1.2 Reference:
/// - Section 4 Runtime Services
/// - Section 6.2 Resolve Parking Session
///
/// ExitPass v1.2 Invariant:
/// - Vendor PMS credentials and endpoints are configuration-owned and must never be hardcoded in source.
/// </summary>
public sealed class HikCentralOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the HikCentral adapter is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the HikCentral Professional OpenAPI base URL.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the HikCentral app key.
    /// </summary>
    public string? AppKey { get; set; }

    /// <summary>
    /// Gets or sets the HikCentral app secret.
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// Gets or sets the HikCentral userId header value.
    /// </summary>
    public string? UserId { get; set; } = "exitpass-adapter";

    /// <summary>
    /// Validates configured HikCentral settings without returning secret values.
    /// </summary>
    /// <returns>Stable, secret-free validation errors.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enabled)
        {
            return errors;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("https" or "http"))
        {
            errors.Add("HIKCENTRAL_BASE_URL_INVALID");
        }
        else if (baseUri.Scheme == "http" && !baseUri.IsLoopback)
        {
            errors.Add("HIKCENTRAL_BASE_URL_MUST_USE_HTTPS");
        }

        if (string.IsNullOrWhiteSpace(AppKey))
        {
            errors.Add("HIKCENTRAL_APP_KEY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(AppSecret))
        {
            errors.Add("HIKCENTRAL_APP_SECRET_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(UserId) ||
            UserId.Length > 32 ||
            UserId.IndexOfAny(['\'', '/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            errors.Add("HIKCENTRAL_USER_ID_INVALID");
        }

        return errors;
    }
}
