using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the legacy Operator Console payable-basis application route is not exposed.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests
{
    private const string LegacyRoutePattern = "/v1/ops/operator-console/statutory-discounts/{validationId:guid}/apply-payable-basis";
    private const string LegacyPath = "/v1/ops/operator-console/statutory-discounts/77000000-0000-0000-0000-000000000090/apply-payable-basis";

    private static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid ServiceIdentityId = Guid.Parse("77000000-0000-0000-0000-000000000020");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000040");

    [Fact]
    public void ApplyPayableBasisEndpointRoute_IsRemoved()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == LegacyRoutePattern)
            .ToArray();

        endpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPayableBasisEndpoint_IsAbsentFromSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().NotContain("/v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis");
        swaggerJson.Should().NotContain("ApplyOperatorConsoleStatutoryDiscountPayableBasis");
        swaggerJson.Should().NotContain("OperatorConsoleStatutoryDiscountPayableBasisApply");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("statutory-discounts.decision.approve", false)]
    [InlineData("statutory-discounts.decision.reject", false)]
    [InlineData("statutory-discounts.payable-basis.apply", false)]
    [InlineData("reconciliation.manage", false)]
    [InlineData("statutory-discounts.payable-basis.apply", true)]
    public async Task LegacyApplyPayableBasisPath_ReturnsNotFound(string? permission, bool servicePrincipal)
    {
        using var factory = new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            });
        using var client = factory.CreateClient();

        if (!string.IsNullOrWhiteSpace(permission))
        {
            client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
        }

        if (servicePrincipal)
        {
            client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, ServiceIdentityId.ToString());
        }
        else if (!string.IsNullOrWhiteSpace(permission))
        {
            client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        }

        using var response = await client.PostAsJsonAsync(LegacyPath, new
        {
            userId = UserId,
            siteId = SiteId,
            siteGroupId = SiteGroupId,
            idempotencyKey = "i009-legacy-operator-console-apply",
            correlationId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
