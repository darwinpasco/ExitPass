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

public sealed class ManagementPlatformStatutoryEvidenceGovernanceApiIntegrationTests
{
    private const string GovernancePath = "/v1/ops/management-platform/statutory-discounts/evidence-governance";
    private static readonly Guid UserId = Guid.Parse("91400000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("91400000-0000-0000-0000-000000000101");
    private static readonly Guid SiteGroupId = Guid.Parse("91400000-0000-0000-0000-000000000201");
    private static readonly Guid CorrelationId = Guid.Parse("91400000-0000-0000-0000-000000000301");

    [Fact]
    public async Task GetGovernance_WhenAuthorized_ReturnsBrowserSafeConfiguration()
    {
        var service = new FakeGovernanceService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync($"{GovernancePath}/sites/{SiteId}?entitlementType=SENIOR_CITIZEN&captureEnabled=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.LastQuery.Should().NotBeNull();
        service.LastQuery!.ScopeType.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSite);
        service.LastQuery.ScopeReference.Should().Be(SiteId);
        service.LastQuery.EntitlementType.Should().Be("SENIOR_CITIZEN");
        service.LastQuery.CaptureEnabled.Should().BeTrue();
        service.LastQuery.ActorUserId.Should().Be(UserId);
        service.LastQuery.CorrelationId.Should().Be(CorrelationId);

        var body = await response.Content.ReadFromJsonAsync<ManagementPlatformStatutoryEvidenceGovernanceResponse>();
        body.Should().NotBeNull();
        body!.ContractVersion.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.ContractVersion);
        body.RequestedScopeType.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSite);
        body.RequestedScopeReference.Should().Be(SiteId);
        body.CorrelationId.Should().Be(CorrelationId);
        var site = body.Sites.Should().ContainSingle().Subject;
        site.SiteReference.Should().Be(SiteId);
        site.SiteGroupReference.Should().Be(SiteGroupId);
        site.GovernanceStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.ConfiguredPartiallyReady);
        site.ReadinessStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.PartiallyReady);
        site.UploadAuthorizationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.UploadFinalizationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.MalwareScanningExecutionReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Disabled);
        site.SecurePreviewReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.RetentionWorkerReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.DeletionWorkerReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
    }

    [Theory]
    [InlineData("statutory-discounts.evidence.capture")]
    [InlineData("statutory-discounts.evidence.view")]
    [InlineData("statutory-discounts.evidence.review-lock")]
    [InlineData("statutory-discounts.evidence.hold")]
    [InlineData("statutory-discounts.evidence.delete-request")]
    [InlineData("statutory-discount-policy.view")]
    [InlineData("operator-console.statutory-discounts.review")]
    [InlineData("webpay.payments.create")]
    [InlineData("apt.cash.accept")]
    [InlineData("pos.fiscal-documents.issue")]
    public async Task GetGovernance_WhenOnlyRelatedPermissionPresent_ReturnsForbidden(string unrelatedPermission)
    {
        using var factory = CreateFactory(new FakeGovernanceService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, unrelatedPermission);

        using var response = await client.GetAsync(GovernancePath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task GetGovernance_WhenBrowserAssertsCustomPermissionHeader_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeGovernanceService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Management-Platform-Permissions", ManagementPlatformStatutoryEvidenceGovernanceValues.Permission);

        using var response = await client.GetAsync(GovernancePath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Theory]
    [InlineData("sites", "SITE")]
    [InlineData("site-groups", "SITE_GROUP")]
    public async Task GetGovernance_WhenScopeDenied_ReturnsSafeForbiddenEnvelope(string routeSegment, string expectedScope)
    {
        using var factory = CreateFactory(new FakeGovernanceService
        {
            Result = ManagementPlatformStatutoryEvidenceGovernanceResult.Failed(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.ScopeDenied,
                CorrelationId,
                expectedScope == "SITE_GROUP"
                    ? ManagementPlatformStatutoryEvidenceGovernanceValues.SiteGroupScopeDenied
                    : ManagementPlatformStatutoryEvidenceGovernanceValues.SiteScopeDenied,
                "The caller is not authorized for the requested statutory evidence governance scope.")
        });
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        var scopeId = expectedScope == "SITE_GROUP" ? SiteGroupId : SiteId;
        using var response = await client.GetAsync($"{GovernancePath}/{routeSegment}/{scopeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(expectedScope == "SITE_GROUP"
            ? ManagementPlatformStatutoryEvidenceGovernanceValues.SiteGroupScopeDenied
            : ManagementPlatformStatutoryEvidenceGovernanceValues.SiteScopeDenied);
        body.Message.Should().NotContain("SELECT");
        body.Message.Should().NotContain("Exception");
        body.Message.Should().NotContain(SiteId.ToString());
        body.Message.Should().NotContain(SiteGroupId.ToString());
        body.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public async Task GetGovernance_WhenAuthorizedScopeEmpty_ReturnsSafeEmptyResponse()
    {
        using var factory = CreateFactory(new FakeGovernanceService
        {
            Result = ManagementPlatformStatutoryEvidenceGovernanceResult.Failed(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.EmptyAuthorizedScope,
                CorrelationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.EmptyAuthorizedScope,
                "The caller has no authorized statutory evidence governance scope.")
        });
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync(GovernancePath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ManagementPlatformStatutoryEvidenceGovernanceResponse>();
        body!.Sites.Should().BeEmpty();
        body.Warnings.Should().Contain(ManagementPlatformStatutoryEvidenceGovernanceValues.EmptyAuthorizedScope);
    }

    [Fact]
    public async Task GetGovernance_WhenBothSiteAndSiteGroupFiltersAreSupplied_ReturnsSafeBadRequest()
    {
        using var factory = CreateFactory(new FakeGovernanceService());
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync($"{GovernancePath}?siteReference={SiteId}&siteGroupReference={SiteGroupId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.InvalidFilter);
    }

    [Fact]
    public void GovernanceRoutes_UseReadOnlyGetAndDedicatedPolicy()
    {
        using var factory = CreateFactory(new FakeGovernanceService());
        var endpointSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var endpoints = endpointSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is not null &&
                endpoint.RoutePattern.RawText.Contains("/statutory-discounts/evidence-governance", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        endpoints.Should().HaveCount(3);
        endpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.SequenceEqual(new[] { "GET" }));
        endpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()!.PolicyName ==
            ManagementPlatformStatutoryEvidenceGovernanceValues.PolicyName);
    }

    [Fact]
    public async Task GovernanceResponse_DoesNotExposeEvidenceStorageWorkflowOrPaymentInternals()
    {
        using var factory = CreateFactory(new FakeGovernanceService());
        using var client = factory.CreateClient();
        AddAuthorizedHeaders(client);

        using var response = await client.GetAsync(GovernancePath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var flattenedNames = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .Concat(document.RootElement.GetProperty("sites")[0].EnumerateObject().Select(property => property.Name))
            .ToArray();

        flattenedNames.Should().NotContain(name => name.Contains("evidenceSet", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("evidenceItem", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("statutoryRequest", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("decision", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("parkingSession", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("uploadUrl", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("previewUrl", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("objectKey", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("bucket", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("checksumValue", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("declaredChecksum", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("verifiedChecksum", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("credential", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("connectionString", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("reviewer", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("payment", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("plate", StringComparison.OrdinalIgnoreCase));
        flattenedNames.Should().NotContain(name => name.Contains("ticket", StringComparison.OrdinalIgnoreCase));
    }

    private static CustomWebApplicationFactory CreateFactory(FakeGovernanceService service) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IManagementPlatformStatutoryEvidenceGovernanceService>();
                services.AddSingleton<IManagementPlatformStatutoryEvidenceGovernanceService>(service);
            });

    private static void AddAuthorizedHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, ManagementPlatformStatutoryEvidenceGovernanceValues.Permission);
    }

    private sealed class FakeGovernanceService : IManagementPlatformStatutoryEvidenceGovernanceService
    {
        public ManagementPlatformStatutoryEvidenceGovernanceQuery? LastQuery { get; private set; }

        public ManagementPlatformStatutoryEvidenceGovernanceResult? Result { get; init; }

        public Task<ManagementPlatformStatutoryEvidenceGovernanceResult> ReadGovernanceAsync(
            ManagementPlatformStatutoryEvidenceGovernanceQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(Result ?? ManagementPlatformStatutoryEvidenceGovernanceResult.Success(
                new ManagementPlatformStatutoryEvidenceGovernance(
                    ManagementPlatformStatutoryEvidenceGovernanceValues.ContractVersion,
                    query.ScopeType,
                    query.ScopeReference,
                    query.CorrelationId,
                    DateTimeOffset.Parse("2026-08-04T08:00:00Z"),
                    ManagementPlatformStatutoryEvidenceGovernanceValues.Fresh,
                    false,
                    [
                        new ManagementPlatformStatutoryEvidenceGovernanceSite(
                            SiteId,
                            "Synthetic Site",
                            SiteGroupId,
                            "Synthetic Site Group",
                            ["SENIOR_CITIZEN", "PWD"],
                            ManagementPlatformStatutoryEvidenceGovernanceValues.ConfiguredPartiallyReady,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.PartiallyReady,
                            true,
                            true,
                            [
                                new ManagementPlatformStatutoryEvidenceDocumentProfile(
                                    "STATUTORY_ID",
                                    "v1",
                                    "STATUTORY_EVIDENCE_STANDARD",
                                    "v1",
                                    "APPROVED_ENABLED",
                                    true)
                            ],
                            ["image/jpeg", "image/png"],
                            1_048_576,
                            300,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            "S3_COMPATIBLE",
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            "PRIVATE_ACCESS_REQUIRED",
                            "REQUIRED_UNVERIFIED",
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Disabled,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Ready,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
                            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
                            DateTimeOffset.Parse("2026-08-04T08:00:00Z"),
                            DateTimeOffset.Parse("2026-08-04T07:55:00Z"),
                            ManagementPlatformStatutoryEvidenceGovernanceValues.Fresh,
                            false,
                            false,
                            $"I014-{query.CorrelationId:D}",
                            [
                                ManagementPlatformStatutoryEvidenceGovernanceValues.WarningMalwareScanningNotImplemented,
                                ManagementPlatformStatutoryEvidenceGovernanceValues.WarningSecurePreviewNotImplemented,
                                ManagementPlatformStatutoryEvidenceGovernanceValues.WarningRetentionWorkerNotImplemented,
                                ManagementPlatformStatutoryEvidenceGovernanceValues.WarningDeletionWorkerNotImplemented,
                                ManagementPlatformStatutoryEvidenceGovernanceValues.WarningObjectReconciliationNotImplemented
                            ],
                            [])
                    ],
                    [
                        ManagementPlatformStatutoryEvidenceGovernanceValues.WarningMalwareScanningNotImplemented,
                        ManagementPlatformStatutoryEvidenceGovernanceValues.WarningSecurePreviewNotImplemented,
                        ManagementPlatformStatutoryEvidenceGovernanceValues.WarningRetentionWorkerNotImplemented,
                        ManagementPlatformStatutoryEvidenceGovernanceValues.WarningDeletionWorkerNotImplemented,
                        ManagementPlatformStatutoryEvidenceGovernanceValues.WarningObjectReconciliationNotImplemented
                    ],
                    [])));
        }
    }
}
