namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Runtime settings for the HikCentral gate action preparation boundary.
/// </summary>
public sealed class HikCentralGateActionOptions
{
    private static readonly char[] UserIdForbiddenCharacters = ['\'', '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Configuration section name for HikCentral gate action settings.
    /// </summary>
    public const string SectionName = "GateIntegrations:HikCentral";

    /// <summary>
    /// HikCentral OpenAPI base URL. Not required for fake transport tests.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// HikCentral app key sent as X-Ca-Key.
    /// </summary>
    public string? AppKey { get; set; }

    /// <summary>
    /// HikCentral app secret used only to calculate the local HMAC signature.
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// HikCentral userId header value required by the door control endpoint.
    /// </summary>
    public string? UserId { get; set; } = "exitpass-gate-integration";

    /// <summary>
    /// Hard gate that must be explicitly enabled before live HikCentral HTTP transport can be registered.
    /// </summary>
    public bool LiveTransportEnabled { get; set; }

    /// <summary>
    /// Request timeout prepared for a future live transport.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Transport mode. Defaults to fake so the live HikCentral path cannot be selected implicitly.
    /// </summary>
    public string TransportMode { get; set; } = "Fake";

    /// <summary>
    /// Validates options required for live HikCentral transport without exposing secret values.
    /// </summary>
    public IReadOnlyList<string> ValidateForLiveTransport()
    {
        var errors = new List<string>();

        if (!LiveTransportEnabled)
        {
            errors.Add("HIKCENTRAL_LIVE_TRANSPORT_DISABLED");
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

        if (RequestTimeoutSeconds is < 1 or > 60)
        {
            errors.Add("HIKCENTRAL_REQUEST_TIMEOUT_SECONDS_INVALID");
        }

        if (string.IsNullOrWhiteSpace(UserId) ||
            UserId.Length > 32 ||
            UserId.IndexOfAny(UserIdForbiddenCharacters) >= 0)
        {
            errors.Add("HIKCENTRAL_USER_ID_INVALID");
        }

        return errors;
    }
}
