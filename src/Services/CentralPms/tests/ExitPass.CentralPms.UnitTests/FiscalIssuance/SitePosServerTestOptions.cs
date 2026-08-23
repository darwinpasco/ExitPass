using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

internal static class SitePosServerTestOptions
{
    internal static readonly Guid SitePosServerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    internal const string SitePosServerRef = "site-pos-server-main";

    internal static FiscalIssuancePosServerIntegrationOptions AddEndpoint(
        this FiscalIssuancePosServerIntegrationOptions options,
        string baseUrl = "https://pos-server.local",
        string environment = "Test")
    {
        options.RuntimeEnvironment = environment;
        options.Endpoints =
        [
            new SitePosServerEndpointOptions
            {
                SitePosServerId = SitePosServerId,
                SitePosServerRef = SitePosServerRef,
                BaseUrl = baseUrl,
                ApiKeyFile = "test-pos-api-key",
                Environment = environment,
                Enabled = true
            }
        ];
        return options;
    }
}
