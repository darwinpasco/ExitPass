using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using ExitPass.CentralPms.Infrastructure.VendorParking.Routing;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class VendorPmsAdapterSelectionTests
{
    [Fact]
    public void MissingProvider_FailsClosedWithoutMockFallback()
    {
        var services = new ServiceCollection();
        var act = () => services.AddCentralPmsVendorPmsAdapter(new ConfigurationBuilder().Build(), "Host=x", "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported*");
    }

    [Fact]
    public void ExplicitMock_UsesMockAndDisablesProjection()
    {
        var configuration = Config(new() { ["CentralPms:VendorPms:Provider"] = "MOCK" });
        var services = new ServiceCollection();
        services.AddCentralPmsVendorPmsAdapter(configuration, "Host=x", "Testing");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IVendorPmsParkingResolutionClient>()
            .Should().BeOfType<MockVendorPmsParkingResolutionClient>();
        provider.GetRequiredService<IVendorSessionProjectionSyncService>()
            .Should().BeOfType<DisabledVendorSessionProjectionSyncService>();
    }

    [Fact]
    public void SiteAdapter_RequiresEnvironmentIdentityAndSecretMount()
    {
        var configuration = Config(new() { ["CentralPms:VendorPms:Provider"] = "SITE_ADAPTER" });
        var services = new ServiceCollection();
        var act = () => services.AddCentralPmsVendorPmsAdapter(configuration, "Host=x", "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("SITE_ADAPTER_ROUTING_CONFIGURATION_INVALID");
    }

    [Fact]
    public void ExplicitMock_InProduction_FailsClosed()
    {
        var configuration = Config(new() { ["CentralPms:VendorPms:Provider"] = "MOCK" });
        var services = new ServiceCollection();
        var act = () => services.AddCentralPmsVendorPmsAdapter(configuration, "Host=x", "Production");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("MOCK_VENDOR_PMS_PROVIDER_NOT_ALLOWED_IN_THIS_ENVIRONMENT");
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
