using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Integration coverage for the APT payable-basis readiness facade over shared vendor parking resolution.
/// </summary>
public sealed class AptPayableBasisReadinessApiIntegrationTests
{
    private const string Route = "/v1/terminal-cash-payments/payable-basis";
    private const string ReadPermission = AptHumanPermissionCatalog.PayableBasisRead;
    private static readonly Guid SitePosServerId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task ResolveTicket_WhenReady_ReturnsAuthoritativePayableBasisAndCashReadiness()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var fakeReadiness = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fakeReadiness);
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000001");
        var request = ResolveRequest("TICKET", ticketReference: UniqueLookup("APT-TICKET"), plateNumber: null, correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        response.Headers.GetValues("X-Correlation-Id").Should().Contain(correlationId.ToString("D"));
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        body!.Operation.Should().Be("RESOLVE");
        body.RevalidationOutcome.Should().BeNull();
        body.ParkingSessionId.Should().NotBe(Guid.Empty);
        body.TariffSnapshotId.Should().NotBe(Guid.Empty);
        body.AuthoritativeAmountMinorUnits.Should().Be(10000);
        body.Currency.Should().Be("PHP");
        body.ReadyForCashAcceptance.Should().BeTrue();
        body.CashAcceptanceReadiness.Should().Be("READY");
        body.SessionReadiness.Should().Be("READY");
        body.TariffReadiness.Should().Be("READY");
        body.TerminalCashAvailability.Should().Be("READY");
        body.FiscalReadiness.Should().Be("READY");
        body.SalesInvoiceConfigurationReadiness.Should().Be("READY");
        body.BlockingReasonCodes.Should().BeEmpty();
        body.TicketReference.Should().Be(request.TicketReference);
        body.PlateNumber.Should().Be("PLATE-FROM-TICKET");
        body.CorrelationId.Should().Be(correlationId);
        fakeReadiness.TotalReadinessCalls.Should().Be(1);
        (await CountForbiddenSideEffectsAsync(body.ParkingSessionId, correlationId)).Should().Be(0);
    }

    [Fact]
    public async Task ResolvePlate_WhenReady_ReturnsAuthoritativePayableBasisThroughSharedVendorParkingPath()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000002");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-PLATE"), correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        body!.PlateNumber.Should().Be(request.PlateNumber);
        body.AuthoritativeAmountMinorUnits.Should().Be(10000);
        body.TariffCalculatedAt.Should().Be(DateTimeOffset.Parse("2030-04-01T01:30:00Z"));
        body.VendorSystemId.Should().NotBeNullOrWhiteSpace();
        body.ReadyForCashAcceptance.Should().BeTrue();
        (await CountForbiddenSideEffectsAsync(body.ParkingSessionId, correlationId)).Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenVendorTemporarilyUnavailable_ReturnsRetryableSafeEnvelope()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000003");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: "UNAVAILABLE", correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("VENDOR_UNAVAILABLE");
        body.Retryable.Should().BeTrue();
        body.Message.Should().NotContain("HikCentral");
    }

    [Fact]
    public async Task Resolve_WhenSalesInvoiceConfigurationIncomplete_BlocksCashAcceptanceWithoutFiscalMutation()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService
        {
            ReadinessStatus = "MISSING_REQUIRED_FIELDS",
            IsComplete = false
        });
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000004");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-FISCAL"), correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        body!.ReadyForCashAcceptance.Should().BeFalse();
        body.FiscalReadiness.Should().Be("BLOCKED");
        body.BlockingReasonCodes.Should().Contain("SALES_INVOICE_CONFIGURATION_NOT_READY");
        body.BlockingReasonCodes.Should().Contain("SALES_INVOICE_CONFIGURATION_INCOMPLETE");
        (await CountForbiddenSideEffectsAsync(body.ParkingSessionId, correlationId)).Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenTerminalCashRailUnavailable_ReturnsSeparateTerminalCashAvailabilityBlock()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        var fakeEligibility = new FakeTerminalCashEligibilityReader(
            new TerminalCashPayableBasisEligibility(
                false,
                "BLOCKED",
                "CASH_PAYMENT_RAIL_NOT_CONFIGURED",
                false,
                "Active CASH payment rail is not configured."));
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService(), fakeEligibility);
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000005");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-RAIL"), correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        body!.TerminalCashAvailability.Should().Be("BLOCKED");
        body.ReadyForCashAcceptance.Should().BeFalse();
        body.BlockingReasonCodes.Should().Contain("CASH_PAYMENT_RAIL_NOT_CONFIGURED");
        fakeEligibility.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Revalidate_WhenBasisUnchanged_ReturnsPassedUnchangedWithoutPaymentOrFiscalSideEffects()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000006");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-UNCHANGED"), correlationId);
        AddSiteHeader(client, request.SiteId);
        var resolved = await ResolveOkAsync(client, request);

        using var response = await client.PostAsJsonAsync(
            $"{Route}/revalidate",
            RevalidateRequest(resolved, request, expectedAmount: 10000, expectedCurrency: "PHP", correlationId));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        body!.Operation.Should().Be("REVALIDATE");
        body.RevalidationOutcome.Should().Be("PASSED_UNCHANGED");
        body.ReadyForCashAcceptance.Should().BeTrue();
        body.AuthoritativeAmountMinorUnits.Should().Be(10000);
        (await CountForbiddenSideEffectsAsync(body.ParkingSessionId, correlationId)).Should().Be(0);
    }

    [Fact]
    public async Task Revalidate_WhenExpectedAmountDiffers_ReturnsAmountChangedAndRefreshedBasis()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000007");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-AMOUNT"), correlationId);
        AddSiteHeader(client, request.SiteId);
        var resolved = await ResolveOkAsync(client, request);

        using var response = await client.PostAsJsonAsync(
            $"{Route}/revalidate",
            RevalidateRequest(resolved, request, expectedAmount: 9999, expectedCurrency: "PHP", correlationId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        body!.RevalidationOutcome.Should().Be("AMOUNT_CHANGED");
        body.AuthoritativeAmountMinorUnits.Should().Be(10000);
        body.ReadyForCashAcceptance.Should().BeFalse();
        body.BlockingReasonCodes.Should().Contain("AMOUNT_CHANGED");
        (await CountForbiddenSideEffectsAsync(body.ParkingSessionId, correlationId)).Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenSiteHeaderDoesNotMatchRequest_ReturnsForbiddenSite()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000008");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-FORBID"), correlationId);
        AddSiteHeader(client, Guid.NewGuid().ToString("D"));

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("FORBIDDEN_SITE");
    }

    [Fact]
    public async Task Resolve_WhenRbacEnabledAndUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService(), enableRbac: true);
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000009");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-RBAC"), correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task Resolve_WhenRbacEnabledAndPermissionPresent_Succeeds()
    {
        await EnsureTerminalCashPatchAppliedAsync();
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService(), enableRbac: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, ReadPermission);
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000010");
        var request = ResolveRequest("PLATE", ticketReference: null, plateNumber: UniqueLookup("APT-RBAC-OK"), correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resolve_WhenRbacEnabledAndCanonicalAptCashierPermissionPresent_Succeeds()
    {
        var authorizationProbe = new FakePayableBasisReadinessService();
        using var factory = CreateFactory(
            new FakeSalesInvoiceProfileAdministrationService(),
            readinessService: authorizationProbe,
            enableRbac: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(
            CentralPmsRbacPolicyCatalog.PermissionsHeaderName,
            AptHumanPermissionCatalog.CashierOperate);
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000011");
        var request = ResolveRequest(
            "TICKET",
            ticketReference: UniqueLookup("APT-CASHIER-RBAC-OK"),
            plateNumber: null,
            correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        authorizationProbe.ResolveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_WhenRbacEnabledAndUnrelatedPermissionPresent_RemainsForbidden()
    {
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService(), enableRbac: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "sessions.resolve");
        var correlationId = Guid.Parse("31000000-0000-0000-0000-000000000012");
        var request = ResolveRequest(
            "PLATE",
            ticketReference: null,
            plateNumber: UniqueLookup("APT-UNRELATED-RBAC"),
            correlationId);
        AddSiteHeader(client, request.SiteId);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_FORBIDDEN");
    }

    [Fact]
    public void AptPayableBasisEndpoints_ExposeTerminalCashPayableBasisReadPolicyMetadata()
    {
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        _ = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/terminal-cash-payments/payable-basis", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .OnlyContain(policy => policy == AptPayableBasisEndpoints.ReadPolicy);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(AptPayableBasisEndpoints.ReadPolicy)
            .Should()
            .Contain(ReadPermission);
    }

    [Fact]
    public async Task HumanSessionAuthority_WhenSiteAndCanonicalDeviceMatch_AllowsRequest()
    {
        var siteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var repository = DeviceRepository(new CentralPmsAptDeviceAuthorizationRecord(
            deviceId,
            "apt-pitx-level-3-01",
            "DEVICE",
            "ExitPass.AssistedPaymentTerminal",
            Active: true,
            SiteAssigned: true));
        var request = HumanSessionRequest(siteId, deviceId);

        var result = await AptPayableBasisEndpoints.ValidateAptAuthorityAsync(
            request,
            repository,
            siteId.ToString("D"),
            "apt-pitx-level-3-01",
            Guid.NewGuid(),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HumanSessionAuthority_WhenCallerNamesForeignSite_FailsBeforeLookup()
    {
        var authorizedSiteId = Guid.NewGuid();
        var requestedSiteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var repository = DeviceRepository(null);
        var request = HumanSessionRequest(authorizedSiteId, deviceId);

        var result = await AptPayableBasisEndpoints.ValidateAptAuthorityAsync(
            request,
            repository,
            requestedSiteId.ToString("D"),
            "apt-pitx-level-3-01",
            Guid.NewGuid(),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be("FORBIDDEN_SITE");
        ((FakeDeviceRbacRepository)repository).AptDeviceLookupCount.Should().Be(0);
    }

    [Fact]
    public async Task HumanSessionAuthority_WhenTerminalDoesNotMatchCanonicalDeviceCode_FailsClosed()
    {
        var siteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var repository = DeviceRepository(new CentralPmsAptDeviceAuthorizationRecord(
            deviceId,
            "apt-pitx-level-3-01",
            "DEVICE",
            "ExitPass.AssistedPaymentTerminal",
            Active: true,
            SiteAssigned: true));
        var request = HumanSessionRequest(siteId, deviceId);

        var result = await AptPayableBasisEndpoints.ValidateAptAuthorityAsync(
            request,
            repository,
            siteId.ToString("D"),
            "apt-other-terminal",
            Guid.NewGuid(),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be("FORBIDDEN_TERMINAL");
    }

    [Fact]
    public async Task HumanSessionAuthority_WhenDeviceHasNoActiveSiteAssignment_FailsClosed()
    {
        var siteId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var repository = DeviceRepository(new CentralPmsAptDeviceAuthorizationRecord(
            deviceId,
            "apt-pitx-level-3-01",
            "DEVICE",
            "ExitPass.AssistedPaymentTerminal",
            Active: true,
            SiteAssigned: false));
        var request = HumanSessionRequest(siteId, deviceId);

        var result = await AptPayableBasisEndpoints.ValidateAptAuthorityAsync(
            request,
            repository,
            siteId.ToString("D"),
            "apt-pitx-level-3-01",
            Guid.NewGuid(),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be("FORBIDDEN_TERMINAL");
    }

    private static CustomWebApplicationFactory CreateFactory(
        FakeSalesInvoiceProfileAdministrationService salesInvoiceReadiness,
        ITerminalCashPayableBasisEligibilityReader? eligibilityReader = null,
        IAptPayableBasisReadinessService? readinessService = null,
        bool enableRbac = false)
    {
        var factory = new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = enableRbac ? "true" : "false",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<ISalesInvoiceProfileAdministrationService>();
                services.AddSingleton<ISalesInvoiceProfileAdministrationService>(salesInvoiceReadiness);
                if (eligibilityReader is not null)
                {
                    services.RemoveAll<ITerminalCashPayableBasisEligibilityReader>();
                    services.AddSingleton(eligibilityReader);
                }

                if (readinessService is not null)
                {
                    services.RemoveAll<IAptPayableBasisReadinessService>();
                    services.AddSingleton(readinessService);
                }
            });

        return factory;
    }

    private static HttpRequest HumanSessionRequest(Guid siteId, Guid deviceId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("site_id", siteId.ToString("D")),
            new Claim("device_service_identity_id", deviceId.ToString("D"))
        ], HumanSessionAuthenticationHandler.SchemeName));
        return context.Request;
    }

    private static ICentralPmsRbacRepository DeviceRepository(CentralPmsAptDeviceAuthorizationRecord? record)
        => new FakeDeviceRbacRepository(record);

    private sealed class FakeDeviceRbacRepository(CentralPmsAptDeviceAuthorizationRecord? record)
        : ICentralPmsRbacRepository
    {
        public int AptDeviceLookupCount { get; private set; }

        public Task<CentralPmsAptDeviceAuthorizationRecord?> GetAptDeviceAuthorizationAsync(
            Guid serviceIdentityId,
            Guid siteId,
            CancellationToken cancellationToken)
        {
            AptDeviceLookupCount++;
            return Task.FromResult(record);
        }

        public Task<bool> UserHasAnyPermissionAsync(
            Guid userId,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> ServiceIdentityIsActiveAsync(
            Guid serviceIdentityId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task RecordDeniedAsync(
            string policyName,
            Guid? userId,
            Guid? serviceIdentityId,
            Guid? correlationId,
            string requestPath,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RecordAuditEventAsync(
            string eventType,
            string eventResult,
            string eventReasonCode,
            string targetEntityType,
            Guid? targetEntityId,
            Guid? actorUserId,
            Guid? actorServiceIdentityId,
            Guid? correlationId,
            string summary,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static async Task<AptPayableBasisReadinessResponse> ResolveOkAsync(
        HttpClient client,
        AptPayableBasisResolveRequest request)
    {
        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var body = await response.Content.ReadFromJsonAsync<AptPayableBasisReadinessResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static AptPayableBasisResolveRequest ResolveRequest(
        string referenceType,
        string? ticketReference,
        string? plateNumber,
        Guid correlationId) =>
        new(
            SiteGroupId: CorrelatedGuid("21000000", correlationId),
            SiteId: CorrelatedGuid("22000000", correlationId),
            SitePosServerId: SitePosServerId.ToString("D"),
            TerminalId: "APT-TERMINAL-001",
            VendorSystemId: CorrelatedGuid("23000000", correlationId),
            ReferenceType: referenceType,
            TicketReference: ticketReference,
            PlateNumber: plateNumber,
            StatutoryDiscountDecisionCommandId: null,
            CorrelationId: correlationId);

    private static AptPayableBasisRevalidateRequest RevalidateRequest(
        AptPayableBasisReadinessResponse resolved,
        AptPayableBasisResolveRequest original,
        long expectedAmount,
        string expectedCurrency,
        Guid correlationId) =>
        new(
            ParkingSessionId: resolved.ParkingSessionId.ToString("D"),
            TariffSnapshotId: resolved.TariffSnapshotId.ToString("D"),
            SiteGroupId: original.SiteGroupId,
            SiteId: original.SiteId,
            SitePosServerId: original.SitePosServerId,
            TerminalId: original.TerminalId,
            VendorSystemId: original.VendorSystemId,
            TicketReference: original.TicketReference,
            PlateNumber: original.PlateNumber,
            ExpectedAmountMinorUnits: expectedAmount,
            ExpectedCurrency: expectedCurrency,
            StatutoryDiscountDecisionCommandId: null,
            CorrelationId: correlationId);

    private static void AddSiteHeader(HttpClient client, string siteId) =>
        client.DefaultRequestHeaders.Add("X-Site-Id", siteId);

    private static string CorrelatedGuid(string prefix, Guid correlationId)
    {
        var suffix = correlationId.ToString("N")[^12..];
        return $"{prefix}-0000-0000-0000-{suffix}";
    }

    private static string UniqueLookup(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..32];

    private static async Task EnsureTerminalCashPatchAppliedAsync()
    {
        var patchPath = ResolveRepositoryPath(
            "infra",
            "db",
            "patches",
            "ExitPass_TerminalCashPaymentCommandReadback_v1.3.sql");
        await ExecuteAsync(await File.ReadAllTextAsync(patchPath));
        await ExecuteAsync("""
            INSERT INTO identity.service_identities (
                service_identity_id,
                service_identity_code,
                service_identity_name,
                identity_type,
                identity_status,
                owning_service_name,
                credential_reference,
                credential_type,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                '23000000-0000-0000-0000-000000000001',
                'APT_READINESS_MOCK_ADAPTER',
                'APT readiness mock adapter',
                'DEVICE',
                'ACTIVE',
                'ExitPass.CentralPms.IntegrationTests',
                NULL,
                'NONE',
                NOW() - INTERVAL '1 minute',
                NOW(),
                '23000000-0000-0000-0000-000000000001',
                NOW(),
                '23000000-0000-0000-0000-000000000001',
                1
            )
            ON CONFLICT (service_identity_id) DO NOTHING;
            """);
    }

    private static string ResolveRepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountForbiddenSideEffectsAsync(Guid parkingSessionId, Guid correlationId)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.terminal_cash_payment_commands WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.payment_confirmations pc
                    INNER JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                    WHERE pa.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.fiscal_issuance_references WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id);
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        var baseCount = (long)(await command.ExecuteScalarAsync() ?? 0L);

        return baseCount + await CountGateCommandsIfPresentAsync(connection, correlationId);
    }

    private static async Task<long> CountGateCommandsIfPresentAsync(
        NpgsqlConnection connection,
        Guid correlationId)
    {
        await using var existsCommand = new NpgsqlCommand(
            "SELECT to_regclass('gates.gate_commands') IS NOT NULL;",
            connection);
        var exists = (bool)(await existsCommand.ExecuteScalarAsync() ?? false);
        if (!exists)
        {
            return 0L;
        }

        await using var countCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM gates.gate_commands WHERE correlation_id = @correlation_id;",
            connection);
        countCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        return (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
    }

    private sealed class FakeTerminalCashEligibilityReader : ITerminalCashPayableBasisEligibilityReader
    {
        private readonly TerminalCashPayableBasisEligibility _eligibility;

        public FakeTerminalCashEligibilityReader(TerminalCashPayableBasisEligibility eligibility)
        {
            _eligibility = eligibility;
        }

        public int CallCount { get; private set; }

        public Task<TerminalCashPayableBasisEligibility> EvaluateAsync(
            TerminalCashPayableBasisEligibilityRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_eligibility);
        }
    }

    private sealed class FakePayableBasisReadinessService : IAptPayableBasisReadinessService
    {
        public int ResolveCallCount { get; private set; }

        public Task<AptPayableBasisReadinessResult> ResolveAsync(
            AptPayableBasisResolveRequest request,
            CancellationToken cancellationToken)
        {
            ResolveCallCount++;
            var response = new AptPayableBasisReadinessResponse(
                Operation: "RESOLVE",
                RevalidationOutcome: null,
                ParkingSessionId: Guid.Parse("31000000-0000-0000-0000-000000000101"),
                TariffSnapshotId: Guid.Parse("31000000-0000-0000-0000-000000000102"),
                SiteGroupId: Guid.Parse(request.SiteGroupId),
                SiteId: Guid.Parse(request.SiteId),
                SitePosServerId: Guid.Parse(request.SitePosServerId),
                TerminalId: request.TerminalId,
                SiteGroupName: "Test Site Group",
                SiteName: "Test Site",
                TicketReference: request.TicketReference,
                PlateNumber: request.PlateNumber,
                EntryTimestamp: DateTimeOffset.Parse("2030-04-01T00:00:00Z"),
                ParkingStatus: "ACTIVE",
                PaymentStatus: "UNPAID",
                AuthoritativeAmountMinorUnits: 10000,
                Currency: "PHP",
                TariffCalculatedAt: DateTimeOffset.Parse("2030-04-01T01:30:00Z"),
                TariffValidUntil: DateTimeOffset.Parse("2030-04-01T01:35:00Z"),
                FeeValidUntil: DateTimeOffset.Parse("2030-04-01T01:35:00Z"),
                VendorSystemId: request.VendorSystemId,
                ReadinessDimensions: [],
                SessionReadiness: "READY",
                TariffReadiness: "READY",
                PaymentEligibility: "READY",
                TerminalCashAvailability: "READY",
                FiscalReadiness: "READY",
                SalesInvoiceConfigurationReadiness: "READY",
                StatutoryDiscountReadiness: null,
                CashAcceptanceReadiness: "READY",
                ReadyForCashAcceptance: true,
                BlockingReasonCodes: [],
                Retryable: false,
                SafeUserFacingClassification: "READY",
                CorrelationId: request.CorrelationId);

            return Task.FromResult(new AptPayableBasisReadinessResult(
                true,
                response,
                null,
                null,
                (int)HttpStatusCode.OK,
                false,
                request.CorrelationId));
        }

        public Task<AptPayableBasisReadinessResult> RevalidateAsync(
            AptPayableBasisRevalidateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Revalidation is not used by the RBAC authorization probe.");
    }

    private sealed class FakeSalesInvoiceProfileAdministrationService : ISalesInvoiceProfileAdministrationService
    {
        public string ReadinessStatus { get; init; } = ManagementPlatformSalesInvoiceProfileReadinessStatuses.Ready;
        public bool IsComplete { get; init; } = true;
        public PosServerSalesInvoiceProfileAdminOutcome? ForcedOutcome { get; init; }
        public int TotalReadinessCalls { get; private set; }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
            ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalReadinessCalls++;
            var correlationId = context.GetOrCreateCorrelationId();
            if (ForcedOutcome is { } outcome && outcome != PosServerSalesInvoiceProfileAdminOutcome.Succeeded)
            {
                return Task.FromResult(PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>.Failure(
                    outcome,
                    $"safe_{outcome.ToString().ToLowerInvariant()}",
                    "Safe Sales Invoice readiness outcome.",
                    correlationId));
            }

            return Task.FromResult(PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>.Success(
                new ManagementPlatformSalesInvoiceHeaderProfileReadiness(
                    request.SiteId,
                    request.SitePosServerId,
                    request.EffectiveAt,
                    ReadinessStatus,
                    Guid.Parse("30000000-0000-0000-0000-000000000101"),
                    1,
                    Guid.Parse("30000000-0000-0000-0000-000000000102"),
                    ManagementPlatformSalesInvoiceProfileLifecycleStates.Approved,
                    IsComplete,
                    true,
                    IsComplete ? [] : ["birAccreditationNumber"],
                    "VALID",
                    IsComplete ? "COMPLETE" : "INCOMPLETE",
                    "SUPPORTED",
                    "NO_OVERLAP",
                    DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
                    correlationId),
                correlationId,
                200));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
            ManagementPlatformFiscalIdentityMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Fiscal identity mutation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
            Guid fiscalIdentityId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Fiscal identity read is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
            Guid fiscalIdentityId,
            ManagementPlatformFiscalIdentityMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Fiscal identity mutation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
            ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile mutation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile read is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
            ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile list is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile mutation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile validation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile mutation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile mutation is not used by APT payable-basis tests.");

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Sales Invoice profile usage is not used by APT payable-basis tests.");
    }
}
