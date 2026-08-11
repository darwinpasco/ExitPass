using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for Central PMS Vendor PMS Adapter selection.
/// </summary>
public sealed class VendorPmsAdapterSelectionTests
{
    /// <summary>
    /// Verifies mock Vendor PMS remains the default adapter for local development and automated tests.
    /// </summary>
    [Fact]
    public void AddCentralPmsVendorPmsAdapter_WhenNoProviderConfigured_UsesMockAdapter()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddCentralPmsVendorPmsAdapter(configuration);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IVendorPmsParkingResolutionClient>();

        client.Should().BeOfType<MockVendorPmsParkingResolutionClient>();
    }

    /// <summary>
    /// Verifies HikCentral can be selected without exposing HikCentral-specific fields to Central PMS contracts.
    /// </summary>
    [Fact]
    public void AddCentralPmsVendorPmsAdapter_WhenHikCentralConfigured_UsesHikCentralAdapter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CentralPms:VendorPms:Provider"] = "HIKCENTRAL",
                ["CentralPms:VendorPms:HikCentral:BaseUrl"] = "https://hikcentral-config.example.invalid",
                ["CentralPms:VendorPms:HikCentral:AppKey"] = "fake-hikcentral-app-key",
                ["CentralPms:VendorPms:HikCentral:AppSecret"] = "fake-hikcentral-app-secret",
                ["CentralPms:VendorPms:HikCentral:UserId"] = "exitpass-adapter"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHikCentralLiveActivationGate, AllowedActivationGate>();

        services.AddCentralPmsVendorPmsAdapter(configuration);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IVendorPmsParkingResolutionClient>();

        client.Should().BeOfType<HikCentralVendorPmsParkingResolutionClient>();
    }

    /// <summary>
    /// Verifies invalid HikCentral configuration fails with stable, secret-free validation errors.
    /// </summary>
    [Fact]
    public void AddCentralPmsVendorPmsAdapter_WhenHikCentralConfigInvalid_DoesNotLeakSecret()
    {
        const string secret = "do-not-leak-hikcentral-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CentralPms:VendorPms:Provider"] = "HIKCENTRAL",
                ["CentralPms:VendorPms:HikCentral:BaseUrl"] = "not-a-url",
                ["CentralPms:VendorPms:HikCentral:AppKey"] = "fake-hikcentral-app-key",
                ["CentralPms:VendorPms:HikCentral:AppSecret"] = secret,
                ["CentralPms:VendorPms:HikCentral:UserId"] = "exitpass-adapter"
            })
            .Build();
        var services = new ServiceCollection();

        var act = () => services.AddCentralPmsVendorPmsAdapter(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HIKCENTRAL_BASE_URL_INVALID*")
            .Which.Message.Should().NotContain(secret);
    }

    [Fact]
    public async Task HikCentralParkingResolution_WhenActivationGateRejects_DoesNotCallLiveAdapter()
    {
        var activationGate = Substitute.For<IHikCentralLiveActivationGate>();
        activationGate.EnsureActivatedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("HIKCENTRAL_LIVE_ACTIVATION_REQUIRED")));
        var liveAdapter = Substitute.For<IVendorParkingDataClient>();
        var client = new HikCentralVendorPmsParkingResolutionClient(activationGate, liveAdapter);
        var request = new VendorParkingSessionLookupRequest(null, "TEST-TICKET", Guid.NewGuid());

        var act = () => client.ResolveSessionAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("HIKCENTRAL_LIVE_ACTIVATION_REQUIRED");
        await liveAdapter.DidNotReceiveWithAnyArgs()
            .ResolveSessionAsync(default!, default);
    }

    private sealed class AllowedActivationGate : IHikCentralLiveActivationGate
    {
        public Task EnsureActivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
