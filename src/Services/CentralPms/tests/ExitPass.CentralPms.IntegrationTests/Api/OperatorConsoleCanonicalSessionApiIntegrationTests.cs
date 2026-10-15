using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleCanonicalSessionApiIntegrationTests
{
    private const string InvoiceEndpoint = "/v1/ops/operator-console/sessions/ensure-for-invoice-customer-information";
    private const string StatutoryEndpoint = "/v1/ops/operator-console/sessions/ensure-for-statutory-discount";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T04:00:00Z");
    private static readonly Guid UserId = Guid.Parse("32000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("32000000-0000-0000-0000-000000000002");
    private static readonly Guid ClientSuppliedSiteGroupId = Guid.Parse("32000000-0000-0000-0000-000000000003");
    private static readonly Guid VendorSystemId = Guid.Parse("32000000-0000-0000-0000-000000000004");
    private static readonly Guid ParkingSessionId = Guid.Parse("32000000-0000-0000-0000-000000000005");
    private static readonly Guid ProjectionId = Guid.Parse("32000000-0000-0000-0000-000000000006");
    private static readonly Guid CorrelationId = Guid.Parse("32000000-0000-0000-0000-000000000007");
    private static readonly Guid AdapterId = Guid.Parse("32000000-0000-0000-0000-000000000008");
    private static readonly Guid CanonicalSiteGroupId = Guid.Parse("32000000-0000-0000-0000-000000000009");
    private static readonly Guid OtherSiteId = Guid.Parse("32000000-0000-0000-0000-000000000010");

    [Theory]
    [InlineData(InvoiceEndpoint)]
    [InlineData(StatutoryEndpoint)]
    public async Task Ensure_WithDirectSiteScopeAndNoSiteGroupGrant_ForwardsOnlyAuthenticatedSite(string endpoint)
    {
        var harness = CreateHarness();
        using var factory = CreateFactory(harness.Service);
        using var client = factory.CreateClient();
        AddDirectSiteIdentity(client);

        using var response = await client.PostAsJsonAsync(endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        harness.Lookup.Queries.Should().ContainSingle();
        harness.Lookup.Queries[0].SiteId.Should().Be(SiteId);
        harness.Lookup.Queries[0].SiteGroupId.Should().Be(CanonicalSiteGroupId);
        harness.Repository.Commands.Should().ContainSingle();
        typeof(OperatorConsoleCanonicalSessionCommand).GetProperty("SiteGroupId").Should().BeNull();
    }

    [Fact]
    public async Task Ensure_WithoutAuthenticatedSiteScope_IsDeniedBeforeServiceInvocation()
    {
        var harness = CreateHarness();
        using var factory = CreateFactory(harness.Service);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString("D"));

        using var response = await client.PostAsJsonAsync(InvoiceEndpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.ErrorCode.Should().Be("INVALID_OPERATOR_CANONICAL_SESSION_REQUEST");
        error.Message.Should().Be("The authenticated Site scope is required.");
        harness.Lookup.Queries.Should().BeEmpty();
        harness.Repository.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Ensure_ClientSuppliedSiteGroupCannotExpandAuthenticatedSiteAuthority()
    {
        var harness = CreateHarness();
        using var factory = CreateFactory(harness.Service);
        using var client = factory.CreateClient();
        AddDirectSiteIdentity(client);

        using var response = await client.PostAsJsonAsync(InvoiceEndpoint, new
        {
            VendorSystemId,
            LookupMode = "TICKET_REFERENCE",
            TicketReference = "TICKET-3201",
            PlateNumber = (string?)null,
            CorrelationId,
            SiteGroupId = ClientSuppliedSiteGroupId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        harness.Lookup.Queries.Should().ContainSingle();
        harness.Lookup.Queries[0].SiteGroupId.Should().Be(CanonicalSiteGroupId);
        harness.Lookup.Queries[0].SiteGroupId.Should().NotBe(ClientSuppliedSiteGroupId);
        typeof(OperatorConsoleCanonicalSessionCommand).GetProperty("SiteGroupId").Should().BeNull();
    }

    [Fact]
    public async Task Ensure_WhenCanonicalActiveSiteGroupCannotBeResolved_FailsClosed()
    {
        var harness = CreateHarness(includeSiteGroup: false);
        using var factory = CreateFactory(harness.Service);
        using var client = factory.CreateClient();
        AddDirectSiteIdentity(client);

        using var response = await client.PostAsJsonAsync(InvoiceEndpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.ErrorCode.Should().Be("OPERATOR_CANONICAL_SITE_SCOPE_UNRESOLVED");
        harness.Lookup.Queries.Should().BeEmpty();
        harness.Repository.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Ensure_WhenProjectionBelongsToAnotherSite_DoesNotMaterializeSession()
    {
        var harness = CreateHarness(projectionSiteId: OtherSiteId);
        using var factory = CreateFactory(harness.Service);
        using var client = factory.CreateClient();
        AddDirectSiteIdentity(client);

        using var response = await client.PostAsJsonAsync(InvoiceEndpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.ErrorCode.Should().Be("OPERATOR_SESSION_PROJECTION_INVALID");
        harness.Repository.Commands.Should().BeEmpty();
    }

    [Fact]
    public void ApprovedRolePolicies_KeepSiteOperatorSiteOnlyAndOperationsSupervisorSiteCapable()
    {
        ApprovedIdentityRoleCatalog.TryGetPolicy(ApprovedIdentityRoleCatalog.SiteOperator, out var siteOperator)
            .Should().BeTrue();
        siteOperator!.AllowedAssignmentScopes.Should().Equal(ApprovedIdentityRoleCatalog.SiteScope);
        siteOperator.DefaultAssignmentScope.Should().Be(ApprovedIdentityRoleCatalog.SiteScope);
        ApprovedIdentityRoleCatalog.IsScopeAllowed(
            ApprovedIdentityRoleCatalog.SiteOperator,
            ApprovedIdentityRoleCatalog.SiteGroupScope).Should().BeFalse();

        ApprovedIdentityRoleCatalog.IsScopeAllowed(
            ApprovedIdentityRoleCatalog.OperationsSupervisor,
            ApprovedIdentityRoleCatalog.SiteScope).Should().BeTrue();
    }

    private static CustomWebApplicationFactory CreateFactory(IOperatorConsoleCanonicalSessionService service) =>
        new CustomWebApplicationFactory().WithServiceOverrides(services =>
        {
            services.RemoveAll<IOperatorConsoleCanonicalSessionService>();
            services.AddSingleton<IOperatorConsoleCanonicalSessionService>(service);
        });

    private static void AddDirectSiteIdentity(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Operator-User-Id", UserId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString("D"));
    }

    private static OperatorConsoleCanonicalSessionRequest Request() =>
        new(VendorSystemId, "TICKET_REFERENCE", "TICKET-3201", null, CorrelationId);

    private static CanonicalSessionHarness CreateHarness(
        bool includeSiteGroup = true,
        Guid? projectionSiteId = null)
    {
        var lookup = new RecordingProjectionLookup(Projection() with
        {
            SiteId = projectionSiteId ?? SiteId
        });
        var repository = new RecordingCanonicalSessionRepository();
        var configuration = new StubManagementConfigurationRepository(includeSiteGroup);
        var service = new OperatorConsoleCanonicalSessionService(
            lookup,
            repository,
            configuration,
            Options.Create(new VendorSessionProjectionOptions { MaxProjectionAgeMinutes = 1 }),
            new FixedClock());
        return new CanonicalSessionHarness(service, lookup, repository);
    }

    private static VendorSessionProjection Projection() =>
        new(
            ProjectionId,
            VendorSystemId,
            SiteId,
            CanonicalSiteGroupId,
            "LOT-1",
            "PITX Level 3",
            null,
            null,
            null,
            null,
            "IN",
            "VENDOR-RECORD-3201",
            "TICKET-3201",
            "ABC3201",
            Now.AddHours(-1),
            null,
            null,
            null,
            null,
            "HIKCENTRAL_PASSAGEWAY",
            new string('a', 64),
            null,
            Now.AddHours(-1),
            "VENDOR_RECORD_GUID",
            "VENDOR-RECORD-3201",
            Now.AddHours(-1),
            Now,
            Now,
            VendorSessionProjectionStatus.Active,
            CorrelationId,
            Now.AddHours(-1),
            Now)
        {
            SourceAdapterIdentityId = AdapterId,
            VendorVehicleTypeCode = "5",
            CanonicalVehicleTypeCode = "CAR"
        };

    private sealed record CanonicalSessionHarness(
        IOperatorConsoleCanonicalSessionService Service,
        RecordingProjectionLookup Lookup,
        RecordingCanonicalSessionRepository Repository);

    private sealed class RecordingProjectionLookup(VendorSessionProjection projection)
        : IVendorSessionProjectionLookupService
    {
        public List<VendorSessionProjectionLookupQuery> Queries { get; } = [];

        public Task<VendorSessionProjectionLookupResult> LookupAsync(
            VendorSessionProjectionLookupQuery query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(VendorSessionProjectionLookupResult.FoundProjection(
                projection,
                Now,
                Now,
                query.CorrelationId));
        }
    }

    private sealed class RecordingCanonicalSessionRepository : IOperatorConsoleCanonicalSessionRepository
    {
        public List<OperatorConsoleCanonicalSessionPersistenceCommand> Commands { get; } = [];

        public Task<OperatorConsoleCanonicalSessionPersistenceResult> EnsureAsync(
            OperatorConsoleCanonicalSessionPersistenceCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new OperatorConsoleCanonicalSessionPersistenceResult(
                ParkingSessionId,
                ReusedExistingSession: false));
        }
    }

    private sealed class StubManagementConfigurationRepository(bool includeSiteGroup)
        : IManagementConfigurationRepository
    {
        public Task<IReadOnlyList<ManagementSite>> ListSitesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagementSite>>
            ([
                new ManagementSite(
                    SiteId,
                    CanonicalSiteGroupId,
                    "PITX-L3",
                    "PITX Level 3",
                    "PARKING",
                    "Asia/Manila",
                    null,
                    null,
                    "Paranaque",
                    "Metro Manila",
                    "PH",
                    null,
                    "ACTIVE",
                    true,
                    true,
                    Now.AddYears(-1),
                    null,
                    1)
            ]);

        public Task<IReadOnlyList<ManagementSiteGroup>> ListSiteGroupsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagementSiteGroup>>(
                includeSiteGroup
                    ? [new ManagementSiteGroup(CanonicalSiteGroupId, "PITX", "PITX", "Asia/Manila", "PHP", "ACTIVE")]
                    : []);

        public Task<string?> GetSiteTimezoneAsync(Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementSite> SaveSiteAsync(ManagementSite site, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ManagementJurisdiction>> ListJurisdictionsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementJurisdiction> SaveJurisdictionAsync(ManagementJurisdiction jurisdiction, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ManagementStatutoryPolicy>> ListStatutoryPoliciesAsync(Guid? jurisdictionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementStatutoryPolicy> CreateStatutoryPolicyDraftAsync(ManagementStatutoryPolicyDraft draft, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementStatutoryPolicy> ChangeStatutoryPolicyStatusAsync(Guid policyId, long expectedRowVersion, string status, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ManagementTariff>> ListTariffsAsync(Guid? siteId, string? vehicleTypeCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementTariff> SaveTariffDraftAsync(ManagementTariff tariff, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementTariff> VerifyTariffAsync(Guid tariffId, long expectedRowVersion, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementTariff> ActivateTariffAsync(Guid tariffId, long expectedRowVersion, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManagementTariff> RetireTariffAsync(Guid tariffId, long expectedRowVersion, Guid actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
