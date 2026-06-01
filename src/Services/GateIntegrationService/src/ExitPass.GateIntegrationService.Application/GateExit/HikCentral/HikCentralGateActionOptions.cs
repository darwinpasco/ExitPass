namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Runtime settings for the HikCentral gate action preparation boundary.
/// </summary>
public sealed class HikCentralGateActionOptions
{
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
    /// Request timeout prepared for a future live transport.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Transport mode. Defaults to fake so the live HikCentral path cannot be selected implicitly.
    /// </summary>
    public string TransportMode { get; set; } = "Fake";
}
