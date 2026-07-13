using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class ManagementPlatformIdentityRbacInventoryApiIntegrationTests
{
    private const string InventoryPath = "/v1/ops/management-platform/identity-rbac/inventory";

    [Fact]
    public async Task GetInventory_WhenAuthorized_ReturnsSafeInventory()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(
            CentralPmsRbacPolicyCatalog.PermissionsHeaderName,
            "management-platform.identity-rbac.inventory.read");

        using var response = await client.GetAsync(InventoryPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ManagementPlatformIdentityRbacInventoryResponse>();
        body.Should().NotBeNull();
        body!.RoleBundles.Should().Contain(role => role.DisplayName == "System / RBAC Administrator");
        body.Permissions.Should().Contain(permission =>
            permission.PermissionKey == "management-platform.identity-rbac.inventory.read");
        body.PolicyMappings.Should().Contain(mapping =>
            mapping.PolicyName == "ManagementPlatformIdentityRbacInventoryRead");
        body.Gaps.Should().Contain(gap => gap.GapKey == "management-platform-ui-missing");
    }

    [Theory]
    [InlineData("fiscal-issuance.status.read")]
    [InlineData("statutory-discounts.payable-basis.apply")]
    [InlineData("operator-console.policy-import-review.review")]
    public async Task GetInventory_WhenCallerHasOperationalPermissionOnly_Returns403(string permission)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);

        using var response = await client.GetAsync(InventoryPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public void ManagementPlatformIdentityRbacRouteFamily_ExposesOnlyReadEndpoint()
    {
        using var factory = CreateFactory();
        var endpointSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var endpoints = endpointSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/v1/ops/management-platform/identity-rbac",
                StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints.Single().RoutePattern.RawText.Should().Be(InventoryPath);
        endpoints.Single().Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().ContainSingle("GET");
    }

    private static CustomWebApplicationFactory CreateFactory()
    {
        return new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IManagementPlatformIdentityRbacInventoryService>();
                services.AddSingleton<IManagementPlatformIdentityRbacInventoryService>(new FakeInventoryService());
            });
    }

    private sealed class FakeInventoryService : IManagementPlatformIdentityRbacInventoryService
    {
        private static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");

        public Task<ManagementPlatformIdentityRbacInventory> GetInventoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ManagementPlatformIdentityRbacInventory(
                [
                    new ManagementPlatformIdentityUser(
                        UserId,
                        "uat.operator",
                        "UAT Operator",
                        null,
                        "ACTIVE",
                        "SITE_OPERATOR",
                        null,
                        null)
                ],
                [
                    new ManagementPlatformRoleBundle(
                        "system-rbac-administrator",
                        "System / RBAC Administrator",
                        "Owns identity and access administration.",
                        ["management-platform.identity-rbac.inventory.read"],
                        ["Business workflow permissions must be separately granted."],
                        "ExitPass Management Platform -> Identity & RBAC Administration")
                ],
                [
                    new ManagementPlatformPermission(
                        "management-platform.identity-rbac.inventory.read",
                        "Identity/RBAC inventory read",
                        "Administration",
                        "CentralPmsRbacPolicyCatalog",
                        ["ManagementPlatformIdentityRbacInventoryRead"],
                        "implemented",
                        null)
                ],
                [
                    new ManagementPlatformPolicyMapping(
                        "ManagementPlatformIdentityRbacInventoryRead",
                        ["management-platform.identity-rbac.inventory.read"],
                        "Management Platform administration",
                        "implemented",
                        null)
                ],
                [],
                [],
                [],
                [],
                [
                    new ManagementPlatformInventoryGap(
                        "management-platform-ui-missing",
                        "Medium",
                        "The Management Platform UI is not implemented.")
                ],
                DateTimeOffset.Parse("2026-07-13T00:00:00Z")));
    }
}
