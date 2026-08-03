using System.Net;
using System.Net.Http.Json;
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

public sealed class ManagementPlatformStatutoryDiscountPolicyCoverageApiIntegrationTests
{
    private const string CoveragePath = "/v1/ops/management-platform/statutory-discounts/policy-coverage";
    private static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000101");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000201");
    private static readonly Guid CorrelationId = Guid.Parse("77000000-0000-0000-0000-000000000301");

    [Fact]
    public async Task GetPolicyCoverage_WhenAuthorized_ReturnsBrowserSafeCoverage()
    {
        var service = new FakeCoverageService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync($"{CoveragePath}?scopeType=SITE&scopeId={SiteId}&entitlementType=SENIOR_CITIZEN&includeInactive=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.LastQuery.Should().NotBeNull();
        service.LastQuery!.ScopeType.Should().Be("SITE");
        service.LastQuery.ScopeId.Should().Be(SiteId);
        service.LastQuery.ActorUserId.Should().Be(UserId);
        service.LastQuery.IncludeInactive.Should().BeTrue();
        var body = await response.Content.ReadFromJsonAsync<ManagementPlatformStatutoryDiscountPolicyCoverageResponse>();
        body.Should().NotBeNull();
        body!.RequestedScopeType.Should().Be("SITE");
        body.CorrelationId.Should().Be(CorrelationId);
        body.CoverageRows.Should().ContainSingle(row =>
            row.SiteReference == SiteId &&
            row.EntitlementType == "SENIOR_CITIZEN" &&
            row.CoverageClassification == "ACTIVE_COVERED" &&
            row.AuthoritativeCoverageAvailable &&
            row.CanonicalJurisdictionReference == Guid.Parse("77000000-0000-0000-0000-000000000401") &&
            row.CanonicalJurisdictionCode == "QUEZON_CITY" &&
            row.ScopeJurisdictionClassification == "SINGLE_LGU");
    }

    [Fact]
    public async Task GetPolicyCoverage_WhenPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory(new FakeCoverageService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "statutory-discounts.policy.resolve");

        using var response = await client.GetAsync($"{CoveragePath}?scopeType=SITE&scopeId={SiteId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task GetPolicyCoverage_WhenBrowserAssertsCustomPermissionHeader_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeCoverageService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Management-Platform-Permissions", ManagementPlatformStatutoryDiscountPolicyCoverageValues.Permission);

        using var response = await client.GetAsync($"{CoveragePath}?scopeType=SITE&scopeId={SiteId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task GetPolicyCoverage_WhenScopeDenied_ReturnsSafeForbiddenEnvelope()
    {
        using var factory = CreateFactory(new FakeCoverageService
        {
            Result = ManagementPlatformStatutoryDiscountPolicyCoverageResult.Failed(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeDenied,
                CorrelationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeDenied,
                "The caller is not authorized for the requested statutory policy scope.")
        });
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync($"{CoveragePath}?scopeType=SITE_GROUP&scopeId={SiteGroupId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeDenied);
        body.Message.Should().NotContain("SELECT");
        body.Message.Should().NotContain("Exception");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    [Theory]
    [InlineData("", "INVALID_SCOPE_TYPE")]
    [InlineData("scopeType=GARAGE&scopeId=77000000-0000-0000-0000-000000000101", "INVALID_SCOPE_TYPE")]
    [InlineData("scopeType=SITE&scopeId=00000000-0000-0000-0000-000000000000", "INVALID_SCOPE_REFERENCE")]
    public async Task GetPolicyCoverage_WhenRequestMalformed_ReturnsSafeBadRequest(string query, string expectedCode)
    {
        using var factory = CreateFactory(new FakeCoverageService());
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync(string.IsNullOrWhiteSpace(query) ? CoveragePath : $"{CoveragePath}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public void ManagementPlatformPolicyCoverageRoute_UsesReadOnlyGetAndDedicatedPolicy()
    {
        using var factory = CreateFactory(new FakeCoverageService());
        var endpointSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var endpoints = endpointSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, CoveragePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints.Single().Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().ContainSingle("GET");
        endpoints.Single().Metadata.GetMetadata<ReconciliationPolicyMetadata>()!.PolicyName
            .Should().Be(ManagementPlatformStatutoryDiscountPolicyCoverageValues.PolicyName);
    }

    private static CustomWebApplicationFactory CreateFactory(FakeCoverageService service) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IManagementPlatformStatutoryDiscountPolicyCoverageService>();
                services.AddSingleton<IManagementPlatformStatutoryDiscountPolicyCoverageService>(service);
            });

    private static void AddAuthorizedHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, ManagementPlatformStatutoryDiscountPolicyCoverageValues.Permission);
    }

    private sealed class FakeCoverageService : IManagementPlatformStatutoryDiscountPolicyCoverageService
    {
        public ManagementPlatformStatutoryDiscountPolicyCoverageQuery? LastQuery { get; private set; }

        public ManagementPlatformStatutoryDiscountPolicyCoverageResult? Result { get; init; }

        public Task<ManagementPlatformStatutoryDiscountPolicyCoverageResult> ReadCoverageAsync(
            ManagementPlatformStatutoryDiscountPolicyCoverageQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(Result ?? ManagementPlatformStatutoryDiscountPolicyCoverageResult.Success(
                new ManagementPlatformStatutoryDiscountPolicyCoverage(
                    query.ScopeType,
                    query.ScopeId,
                    query.ScopeType,
                    query.ScopeId,
                    "Synthetic Site",
                    query.CorrelationId,
                    DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                    [
                        new ManagementPlatformStatutoryDiscountPolicyCoverageRow(
                            SiteId,
                            "Synthetic Site",
                            "SENIOR_CITIZEN",
                            "ACTIVE_COVERED",
                            "ACTIVE",
                            AuthoritativeCoverageAvailable: true,
                            DateOnly.Parse("2026-01-01"),
                            EffectiveTo: null,
                            "SC-ACTIVE",
                            "QC-ORD-001",
                            "QUEZON_CITY",
                            "synthetic-policy-v1",
                            DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                            "COMPLETE",
                            "ACTIVE_POLICY_EFFECTIVE",
                            "INTEGRATION_TEST",
                            Guid.Parse("77000000-0000-0000-0000-000000000401"),
                            "QUEZON_CITY",
                            "Quezon City",
                            "CITY",
                            "METRO_MANILA",
                            "SINGLE_LGU",
                            "FREE_DURATION",
                            "RESIDENT_ONLY",
                            SourceDocumentAvailable: true,
                            "RESEARCH_COVERAGE_IDENTIFIED")
                    ])));
        }
    }
}

