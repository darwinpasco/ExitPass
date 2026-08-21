using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
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

public sealed class ManagementDashboardReportingApiIntegrationTests
{
    private const string CatalogPath = "/v1/management-platform/dashboard/catalog";
    private const string OverviewPath = "/v1/management-platform/dashboard/operational-overview";
    private static readonly Guid UserId = Guid.Parse("93200000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("93200000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("93200000-0000-0000-0000-000000000101");
    private static readonly Guid CorrelationId = Guid.Parse("93200000-0000-0000-0000-000000000301");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-20T08:00:00Z");

    [Fact]
    public async Task Catalog_WhenAuthorized_ReturnsControlledAvailabilityContract()
    {
        var service = new FakeService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementDashboardReportingValues.CatalogPermission);

        using var response = await client.GetAsync(CatalogPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ManagementDashboardCatalogResponse>();
        body!.ContractVersion.Should().Be(ManagementDashboardReportingValues.ContractVersion);
        body.Reports.Should().Contain(report =>
            report.ReportId == ManagementDashboardReportingValues.OperationalOverviewReportId &&
            report.Availability == ManagementDashboardReportingValues.Partial);
        body.Reports.Should().Contain(report =>
            report.ReportId == ManagementDashboardReportingValues.PaymentReconciliationReportId &&
            report.Availability == ManagementDashboardReportingValues.Unavailable);
        service.CatalogActor.Should().Be(new ManagementDashboardActor(UserId, SessionId));
    }

    [Fact]
    public async Task Overview_WhenAuthorized_ReturnsExplicitScopeSourceAndFreshness()
    {
        var service = new FakeService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementDashboardReportingValues.OverviewPermission);

        using var response = await client.GetAsync($"{OverviewPath}?scopeType=SITE&scopeReference={SiteId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ManagementDashboardOperationalOverviewResponse>();
        body!.RequestedScope.ScopeType.Should().Be("SITE");
        body.RequestedScope.ScopeReference.Should().Be(SiteId);
        body.CorrelationId.Should().Be(CorrelationId);
        body.Sections.Should().OnlyContain(section =>
            !string.IsNullOrWhiteSpace(section.SourceAuthority) &&
            !string.IsNullOrWhiteSpace(section.Freshness));
        service.LastQuery.Should().Be(new ManagementDashboardOperationalOverviewQuery("SITE", SiteId, CorrelationId));
    }

    [Theory]
    [InlineData(CatalogPath)]
    [InlineData(OverviewPath + "?scopeType=SITE&scopeReference=93200000-0000-0000-0000-000000000101")]
    public async Task DashboardRoutes_WhenUnauthenticated_Return401(string path)
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode
            .Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Theory]
    [InlineData(CatalogPath, ManagementDashboardReportingValues.OverviewPermission)]
    [InlineData(OverviewPath + "?scopeType=SITE&scopeReference=93200000-0000-0000-0000-000000000101", ManagementDashboardReportingValues.CatalogPermission)]
    [InlineData(OverviewPath + "?scopeType=SITE&scopeReference=93200000-0000-0000-0000-000000000101", "reconciliation.manage")]
    public async Task DashboardRoutes_WhenPermissionMissing_Return403(string path, string unrelatedPermission)
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, unrelatedPermission);

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode
            .Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task DashboardRoute_DoesNotTrustClientAuthoredAuthorityHeaders()
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Management-Platform-User-Id", UserId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Management-Platform-Permissions", ManagementDashboardReportingValues.OverviewPermission);
        client.DefaultRequestHeaders.Add("X-Management-Platform-Site-Id", SiteId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Authorization-Epoch", "999");

        using var response = await client.GetAsync($"{OverviewPath}?scopeType=SITE&scopeReference={SiteId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(ManagementDashboardReportingOutcome.FeatureDisabled, HttpStatusCode.ServiceUnavailable, ManagementDashboardReportingValues.FeatureDisabled)]
    [InlineData(ManagementDashboardReportingOutcome.InvalidScope, HttpStatusCode.BadRequest, ManagementDashboardReportingValues.InvalidScopeType)]
    [InlineData(ManagementDashboardReportingOutcome.SessionInvalid, HttpStatusCode.Unauthorized, ManagementDashboardReportingValues.SessionInvalid)]
    [InlineData(ManagementDashboardReportingOutcome.ScopeNotFoundOrDenied, HttpStatusCode.NotFound, ManagementDashboardReportingValues.ScopeNotFoundOrDenied)]
    [InlineData(ManagementDashboardReportingOutcome.SourceUnavailable, HttpStatusCode.ServiceUnavailable, ManagementDashboardReportingValues.SourceUnavailable)]
    public async Task Overview_UsesSafeProblemContract(
        ManagementDashboardReportingOutcome outcome,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var service = new FakeService
        {
            OverviewResult = ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Failed(
                outcome,
                CorrelationId,
                expectedCode,
                "The dashboard request failed safely.",
                outcome == ManagementDashboardReportingOutcome.SourceUnavailable)
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementDashboardReportingValues.OverviewPermission);

        using var response = await client.GetAsync($"{OverviewPath}?scopeType=SITE&scopeReference={SiteId:D}");

        response.StatusCode.Should().Be(expectedStatus);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(expectedCode);
        body.Message.Should().NotContain("Exception").And.NotContain("SELECT").And.NotContain("password");
    }

    [Fact]
    public void DashboardRouteFamily_ContainsOnlyTwoReadOnlyGetEndpoints()
    {
        using var factory = CreateFactory(new FakeService());
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/v1/management-platform/dashboard",
                StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Should().BeEquivalentTo([CatalogPath, OverviewPath]);
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single())
            .Should().OnlyContain(method => method == "GET");
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>())
            .Should().NotContainNulls();
    }

    [Fact]
    public async Task OverviewContract_DoesNotExposeSensitiveOrMutationFields()
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementDashboardReportingValues.OverviewPermission);

        var json = await client.GetStringAsync($"{OverviewPath}?scopeType=SITE&scopeReference={SiteId:D}");
        using var document = JsonDocument.Parse(json);
        var names = EnumeratePropertyNames(document.RootElement).ToArray();

        names.Should().NotContain(name =>
            name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("plate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("evidence", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("mutation", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var nested in EnumeratePropertyNames(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumeratePropertyNames(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static CustomWebApplicationFactory CreateFactory(FakeService service) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true",
                ["ManagementPlatform:DashboardReporting:Enabled"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IIdentityAdministrationActorAccessor>();
                services.AddSingleton<IIdentityAdministrationActorAccessor>(
                    new FakeActorAccessor(new IdentityAdministrationActor(UserId, SessionId)));
                services.RemoveAll<IManagementDashboardReportingService>();
                services.AddSingleton<IManagementDashboardReportingService>(service);
            });

    private static void AddFixtureAuthorization(HttpClient client, string permission)
    {
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
    }

    private sealed class FakeActorAccessor(IdentityAdministrationActor current) : IIdentityAdministrationActorAccessor
    {
        public IdentityAdministrationActor? Current { get; } = current;
    }

    private sealed class FakeService : IManagementDashboardReportingService
    {
        public ManagementDashboardActor? CatalogActor { get; private set; }
        public ManagementDashboardOperationalOverviewQuery? LastQuery { get; private set; }
        public ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>? OverviewResult { get; init; }

        public Task<ManagementDashboardReportingResult<ManagementDashboardCatalog>> GetCatalogAsync(
            ManagementDashboardActor actor,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            CatalogActor = actor;
            var catalog = new ManagementDashboardCatalog(
                ManagementDashboardReportingValues.ContractVersion,
                Now,
                [
                    CatalogEntry(ManagementDashboardReportingValues.OperationalOverviewReportId, ManagementDashboardReportingValues.Partial),
                    CatalogEntry(ManagementDashboardReportingValues.PaymentReconciliationReportId, ManagementDashboardReportingValues.Unavailable)
                ]);
            return Task.FromResult(ManagementDashboardReportingResult<ManagementDashboardCatalog>.Success(catalog, correlationId));
        }

        public Task<ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>> GetOperationalOverviewAsync(
            ManagementDashboardActor actor,
            ManagementDashboardOperationalOverviewQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            var scope = new ManagementDashboardScope("SITE", SiteId, "Synthetic Site");
            var overview = new ManagementDashboardOperationalOverview(
                ManagementDashboardReportingValues.ContractVersion,
                ManagementDashboardReportingValues.OperationalOverviewReportId,
                scope,
                scope,
                Now,
                Now.AddMinutes(-1),
                ManagementDashboardReportingValues.Partial,
                ManagementDashboardReportingValues.Current,
                query.CorrelationId,
                [
                    new ManagementDashboardOverviewSection(
                        "site-operational-status",
                        "Site operational status",
                        ManagementDashboardReportingValues.Available,
                        ManagementDashboardReportingValues.Current,
                        "CENTRAL_PMS_SITE_REGISTRY",
                        Now.AddMinutes(-1),
                        [new ManagementDashboardMetric("sites-total", "Sites", 1, "COUNT")],
                        [],
                        [])
                ],
                [],
                []);
            return Task.FromResult(OverviewResult ??
                ManagementDashboardReportingResult<ManagementDashboardOperationalOverview>.Success(overview, query.CorrelationId));
        }

        private static ManagementDashboardCatalogEntry CatalogEntry(string id, string availability) =>
            new(
                id,
                ManagementDashboardReportingValues.ContractVersion,
                id,
                "Operations",
                "Synthetic contract entry.",
                ["SITE", "SITE_GROUP"],
                "dashboard.view",
                availability,
                "CENTRAL_PMS",
                "INTERNAL_OPERATIONAL_AGGREGATE",
                ["scopeType", "scopeReference"],
                "Section source timestamp.",
                [],
                []);
    }
}
