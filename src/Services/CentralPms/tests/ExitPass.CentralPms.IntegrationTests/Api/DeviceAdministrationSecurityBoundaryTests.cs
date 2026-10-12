using System.Net;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class DeviceAdministrationSecurityBoundaryTests
{
    [Fact]
    public async Task HostedAdministrationRoutes_DoNotAcceptLegacyFixtureHeadersAsAuthority()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ExitPass-User-Id", Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add("X-ExitPass-Permissions", "device.view,device.manage");
        client.DefaultRequestHeaders.Add("X-ExitPass-Human-Session-Id", Guid.NewGuid().ToString("D"));

        using var list = await client.GetAsync("/v1/management-platform/devices");
        using var scopes = await client.GetAsync("/v1/management-platform/devices/scopes");

        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        scopes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void ListAndDetailsContracts_ExposeNoSecretOrTrustMaterial()
    {
        var safeTypes = new[]
        {
            typeof(DeviceSummaryDto), typeof(DeviceDetailsDto),
            typeof(DeviceProvisioningHistoryDto), typeof(DeviceCredentialHistoryDto),
            typeof(DeviceAdministrativeActionDto), typeof(DeviceAdministrationSiteScopeDto)
        };
        var forbidden = new[] { "Secret", "PrivateKey", "TokenHash", "Fingerprint", "RawCredential", "ProvisioningCredential" };

        safeTypes.SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .Should().NotContain(name => forbidden.Any(term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AdministrativeRequests_CannotSupplyActorOrCredentialVersionAuthority()
    {
        var requestTypes = new[]
        {
            typeof(RegisterDeviceRequest), typeof(DeviceLifecycleRequest), typeof(ReplaceDeviceRequest)
        };
        var forbidden = new[] { "Actor", "Administrator", "CredentialVersion", "ServiceIdentity", "TokenHash" };

        requestTypes.SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .Should().NotContain(name => forbidden.Any(term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
