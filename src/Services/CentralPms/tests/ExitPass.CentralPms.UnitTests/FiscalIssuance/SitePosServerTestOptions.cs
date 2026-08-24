using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

internal static class SitePosServerTestOptions
{
    internal static readonly Guid SiteId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static readonly Guid SitePosServerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    internal const string SitePosServerRef = "site-pos-server-main";

    internal static readonly Guid FiscalDocumentTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000101");
    internal static readonly Guid FiscalDocumentStatusCodeId = Guid.Parse("10000000-0000-0000-0000-000000000102");
    internal static readonly Guid FiscalLineTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000201");
    internal static readonly Guid FiscalTenderTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000301");
    internal static readonly Guid FiscalTaxTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000401");
    internal static readonly Guid FiscalTaxClassificationCodeId = Guid.Parse("10000000-0000-0000-0000-000000000402");
    internal static readonly Guid FiscalDiscountPrivilegeTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000501");
    internal static readonly Guid FiscalTotalTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000601");

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
                SiteId = SiteId,
                SitePosServerId = SitePosServerId,
                SitePosServerRef = SitePosServerRef,
                BaseUrl = baseUrl,
                ApiKeyFile = "test-pos-api-key",
                Environment = environment,
                Enabled = true,
                FiscalDocumentTypeCodeId = FiscalDocumentTypeCodeId,
                FiscalDocumentStatusCodeId = FiscalDocumentStatusCodeId,
                FiscalLineTypeCodeId = FiscalLineTypeCodeId,
                FiscalTenderTypeCodeId = FiscalTenderTypeCodeId,
                FiscalTaxTypeCodeId = FiscalTaxTypeCodeId,
                FiscalTaxClassificationCodeId = FiscalTaxClassificationCodeId,
                FiscalDiscountPrivilegeTypeCodeId = FiscalDiscountPrivilegeTypeCodeId,
                FiscalTotalTypeCodeId = FiscalTotalTypeCodeId
            }
        ];
        return options;
    }
}
