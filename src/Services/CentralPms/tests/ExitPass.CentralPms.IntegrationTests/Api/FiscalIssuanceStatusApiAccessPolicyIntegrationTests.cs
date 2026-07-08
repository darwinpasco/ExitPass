using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies access policy hardening for the read-only fiscal issuance status endpoint.
/// </summary>
public sealed class FiscalIssuanceStatusApiAccessPolicyIntegrationTests
{
    private static readonly Guid ReferenceId = Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec0");
    private const string StatusReadPermission = "fiscal-issuance.status.read";

    [Fact]
    public async Task StatusRead_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeFiscalIssuanceStatusReadService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/fiscal-issuance/references/{ReferenceId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task StatusRead_WhenRbacEnabledAndPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory(new FakeFiscalIssuanceStatusReadService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "reconciliation.evaluate");

        using var response = await client.GetAsync($"/v1/fiscal-issuance/references/{ReferenceId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public async Task StatusRead_WhenRbacEnabledAndStatusPermissionPresent_ReturnsRecordedStatus()
    {
        var fake = new FakeFiscalIssuanceStatusReadService { Result = ReadModel(ReferenceId) };
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        AddStatusReadPermission(client);

        using var response = await client.GetAsync($"/v1/fiscal-issuance/references/{ReferenceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FiscalIssuanceStatusResponse>();
        body.Should().NotBeNull();
        body!.FiscalIssuanceReferenceId.Should().Be(ReferenceId);
        body.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_RECORDED");
        body.FiscalDocumentNumber.Should().Be("SI-00000001-UAT");
        fake.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task StatusRead_WhenAuthorizedAndReferenceMissing_ReturnsNotFound()
    {
        using var factory = CreateFactory(new FakeFiscalIssuanceStatusReadService());
        using var client = factory.CreateClient();
        AddStatusReadPermission(client);

        using var response = await client.GetAsync($"/v1/fiscal-issuance/references/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("FISCAL_ISSUANCE_REFERENCE_NOT_FOUND");
    }

    [Fact]
    public async Task StatusRead_RemainsGetOnly()
    {
        using var factory = CreateFactory(new FakeFiscalIssuanceStatusReadService { Result = ReadModel(ReferenceId) });
        using var client = factory.CreateClient();
        AddStatusReadPermission(client);

        using var response = await client.PostAsync($"/v1/fiscal-issuance/references/{ReferenceId}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public void StatusReadEndpoint_ExposesFiscalIssuanceStatusReadPolicyMetadata()
    {
        using var factory = CreateFactory(new FakeFiscalIssuanceStatusReadService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/fiscal-issuance/references", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName
            .Should()
            .Be("FiscalIssuanceStatusRead");
    }

    private static CustomWebApplicationFactory CreateFactory(FakeFiscalIssuanceStatusReadService fake)
    {
        return new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IFiscalIssuanceStatusReadService>();
                services.AddSingleton<IFiscalIssuanceStatusReadService>(fake);
            });
    }

    private static void AddStatusReadPermission(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, StatusReadPermission);
    }

    private static FiscalIssuanceStatusReadModel ReadModel(Guid referenceId)
    {
        var now = DateTimeOffset.Parse("2026-07-08T10:00:00+08:00");
        return new FiscalIssuanceStatusReadModel(
            FiscalIssuanceReferenceId: referenceId,
            FiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
            ResultClassification: "NEWLY_CREATED",
            FiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
            FiscalNumberAssignmentState: "ASSIGNED",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
            PaymentConfirmationId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec1"),
            PaymentAttemptId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec2"),
            ParkingSessionId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec3"),
            SiteId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec4"),
            SitePosServerId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec5"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            FiscalDocumentTypeCodeId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec6"),
            FiscalDocumentTypeCodeKey: "sales_invoice",
            PosServerFiscalDocumentId: Guid.Parse("deac11e4-fc31-4c40-9a44-da690b9730ef"),
            FiscalDocumentNumber: "SI-00000001-UAT",
            FiscalIdentityId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec7"),
            FiscalSequencePolicyId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec8"),
            FiscalSequenceValue: 1,
            FiscalSeries: "central-pms-uat-si-sequence-policy",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: now,
            FiscalNumberAssignedByRef: "pos-server",
            SemanticRequestHashValue: "ea863d4f8dc2c11e061236bec63855a26e896e700b4de92e5666bf8ee78cd38d",
            SemanticRequestHashVersion: "sha256:v1",
            SemanticRequestHashStatus: "AVAILABLE",
            SemanticRequestHashAlgorithm: "SHA-256",
            SemanticRequestHashSourceFactCount: 24,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            LatestExceptionReason: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            CorrelationId: Guid.Parse("bf5288c9-426c-4f22-9567-ac5efac03ec9"));
    }

    private sealed class FakeFiscalIssuanceStatusReadService : IFiscalIssuanceStatusReadService
    {
        public FiscalIssuanceStatusReadModel? Result { get; init; }

        public int CallCount { get; private set; }

        public Task<FiscalIssuanceStatusReadModel?> GetByReferenceIdAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result?.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId ? Result : null);
        }
    }
}
