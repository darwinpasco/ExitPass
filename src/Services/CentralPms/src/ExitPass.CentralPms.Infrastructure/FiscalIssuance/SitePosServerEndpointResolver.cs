using ExitPass.CentralPms.Application.FiscalIssuance;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

internal sealed class SitePosServerEndpointResolver
{
    private readonly FiscalIssuancePosServerIntegrationOptions _options;

    public SitePosServerEndpointResolver(IOptions<FiscalIssuancePosServerIntegrationOptions> options)
    {
        _options = options.Value;
    }

    public SitePosServerEndpointResolution Resolve(PosServerRoutingContext routingContext)
    {
        ArgumentNullException.ThrowIfNull(routingContext);

        var matches = _options.Endpoints
            .Where(endpoint => endpoint.SitePosServerId == routingContext.SitePosServerId)
            .ToArray();

        if (matches.Length != 1)
        {
            return SitePosServerEndpointResolution.Failed(
                matches.Length == 0
                    ? "site_pos_server_endpoint_not_found"
                    : "site_pos_server_endpoint_ambiguous");
        }

        var endpoint = matches[0];
        if (!string.Equals(
                endpoint.SitePosServerRef?.Trim(),
                routingContext.SitePosServerRef,
                StringComparison.Ordinal))
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_endpoint_identity_mismatch");
        }

        if (!endpoint.Enabled)
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_endpoint_disabled");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Environment) ||
            !string.Equals(
                endpoint.Environment.Trim(),
                _options.RuntimeEnvironment?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_endpoint_environment_mismatch");
        }

        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            baseUri.AbsolutePath != "/")
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_endpoint_url_invalid");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !IsLocalEnvironment(_options.RuntimeEnvironment))
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_endpoint_https_required");
        }

        if (string.IsNullOrWhiteSpace(endpoint.ApiKeyFile) || !File.Exists(endpoint.ApiKeyFile))
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_api_key_file_unavailable");
        }

        string apiKey;
        try
        {
            apiKey = File.ReadAllText(endpoint.ApiKeyFile).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_api_key_file_unavailable");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return SitePosServerEndpointResolution.Failed("site_pos_server_api_key_file_empty");
        }

        return SitePosServerEndpointResolution.Success(baseUri, apiKey);
    }

    private static bool IsLocalEnvironment(string? environment) =>
        string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment, "SecureDevelopment", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment, "IntegrationTest", StringComparison.OrdinalIgnoreCase);
}

internal sealed record SitePosServerEndpointResolution(
    bool IsSuccess,
    string Code,
    Uri? BaseUri,
    string? ApiKey)
{
    public static SitePosServerEndpointResolution Success(Uri baseUri, string apiKey) =>
        new(true, "site_pos_server_endpoint_resolved", baseUri, apiKey);

    public static SitePosServerEndpointResolution Failed(string code) =>
        new(false, code, null, null);
}
