using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class ManagementPlatformSalesInvoiceProfileApiIntegrationTests
{
    private static readonly Guid UserId = Guid.Parse("8a000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("8a000000-0000-0000-0000-000000000101");
    private static readonly Guid OtherSiteId = Guid.Parse("8a000000-0000-0000-0000-000000000102");
    private static readonly Guid SitePosServerId = Guid.Parse("8a000000-0000-0000-0000-000000000201");
    private static readonly Guid FiscalIdentityId = Guid.Parse("8a000000-0000-0000-0000-000000000301");
    private static readonly Guid ProfileId = Guid.Parse("8a000000-0000-0000-0000-000000000401");
    private static readonly Guid CorrelationId = Guid.Parse("8a000000-0000-0000-0000-000000000501");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_AnonymousCallerIsRejected()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/management-platform/fiscal-identities/{FiscalIdentityId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fake.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_ReadPolicyRejectsUnrelatedPermission()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "reconciliation.view");

        using var response = await client.GetAsync($"/v1/management-platform/fiscal-identities/{FiscalIdentityId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fake.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_ReadAuthorizedCallerCanReadValidateReadinessAndUsage()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService
        {
            ReadinessStatus = "FUTURE_SAFE_STATUS"
        };
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.read");

        using var profileResponse = await client.GetAsync($"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}");
        using var validateResponse = await client.PostAsync($"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}/validate", JsonContent.Create(new { }));
        using var readinessResponse = await client.GetAsync($"/v1/management-platform/sales-invoice-header-profiles/effective-readiness?siteId={SiteId:D}&sitePosServerId={SitePosServerId:D}&effectiveAt=2026-07-19T00:00:00Z");
        using var usageResponse = await client.GetAsync($"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}/usage");

        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await profileResponse.Content.ReadFromJsonAsync<ManagementPlatformSalesInvoiceHeaderProfileDto>(JsonOptions);
        profile!.SupplierDeveloperRegisteredName.Should().Be("Governed Test Software Supplier Inc.");
        profile.SupplierDeveloperAddress.Should().Be("456 Software Park, Cebu City");
        profile.SupplierDeveloperTin.Should().Be("987-654-321-000");
        var readiness = await readinessResponse.Content.ReadFromJsonAsync<ManagementPlatformSalesInvoiceHeaderProfileReadinessDto>(JsonOptions);
        readiness!.ResolutionStatus.Should().Be("FUTURE_SAFE_STATUS");
        var usage = await usageResponse.Content.ReadFromJsonAsync<ManagementPlatformSalesInvoiceHeaderProfileUsageDto>(JsonOptions);
        usage!.SafeFiscalDocumentIdentifiers.Should().ContainSingle("DSI-SAFE-0001");
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_ReadOnlyCallerCannotMutate()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.read");

        using var response = await client.PostAsJsonAsync("/v1/management-platform/sales-invoice-header-profiles", CreateProfileRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fake.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_ManageCallerCreatesAndUpdatesWithDerivedActor()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.manage");
        var createRequest = CreateProfileRequest();
        var updateRequest = CreateProfileRequest() with
        {
            BirAccreditationIssuedDate = new DateOnly(2026, 1, 2),
            BirAccreditationValidUntil = new DateOnly(2031, 1, 2),
            PtuIssuedDate = new DateOnly(2026, 1, 3)
        };

        using var createResponse = await client.PostAsJsonAsync("/v1/management-platform/sales-invoice-header-profiles", createRequest);
        using var updateResponse = await client.PatchAsJsonAsync($"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}", updateRequest);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.LastProfileMutationRequest!.RequestedByRef.Should().Be($"central-pms-user:{UserId:D}");
        fake.LastProfileMutationRequest.BirAccreditationIssuedDate.Should().Be(new DateOnly(2026, 1, 2));
        fake.LastProfileMutationRequest.BirAccreditationValidUntil.Should().Be(new DateOnly(2031, 1, 2));
        fake.LastProfileMutationRequest.PtuIssuedDate.Should().Be(new DateOnly(2026, 1, 3));
        fake.LastProfileMutationRequest.SupplierDeveloperRegisteredName.Should().Be("Governed Test Software Supplier Inc.");
        fake.LastProfileMutationRequest.SupplierDeveloperAddress.Should().Be("456 Software Park, Cebu City");
        fake.LastProfileMutationRequest.SupplierDeveloperTin.Should().Be("987-654-321-000");
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_ManagePermissionDoesNotApproveOrRetire()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.manage");

        using var response = await client.PostAsJsonAsync(
            $"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}/approve",
            new { approvedByRef = "browser-supplied-actor" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fake.ApproveCalls.Should().Be(0);
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_ApproveCallerUsesAuthenticatedActorAndIgnoresBrowserActor()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.approve");

        using var approveResponse = await client.PostAsJsonAsync(
            $"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}/approve",
            new { approvedByRef = "browser-supplied-actor" });
        using var retireResponse = await client.PostAsJsonAsync(
            $"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}/retire",
            new { retiredByRef = "browser-supplied-actor", retireAt = "2026-07-19T00:00:00Z" });

        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retireResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.LastApprovalRequest!.ApprovedByRef.Should().Be($"central-pms-user:{UserId:D}");
        fake.LastRetirementRequest!.RetiredByRef.Should().Be($"central-pms-user:{UserId:D}");
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_SiteScopeIsEnforcedForBodyQueryAndResourceResults()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService();
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.manage,sales-invoice-profile.read", authorizedSiteId: SiteId);

        using var createResponse = await client.PostAsJsonAsync(
            "/v1/management-platform/sales-invoice-header-profiles",
            CreateProfileRequest() with { SiteId = OtherSiteId });
        using var listResponse = await client.GetAsync($"/v1/management-platform/sales-invoice-header-profiles?siteId={OtherSiteId:D}&sitePosServerId={SitePosServerId:D}");
        using var readinessResponse = await client.GetAsync($"/v1/management-platform/sales-invoice-header-profiles/effective-readiness?siteId={OtherSiteId:D}&sitePosServerId={SitePosServerId:D}&effectiveAt=2026-07-19T00:00:00Z");

        fake.ProfileSiteId = OtherSiteId;
        using var readResponse = await client.GetAsync($"/v1/management-platform/sales-invoice-header-profiles/{ProfileId:D}");

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        readinessResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        readResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest, HttpStatusCode.BadRequest)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed, HttpStatusCode.BadGateway)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied, HttpStatusCode.BadGateway)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.Conflict, HttpStatusCode.Conflict)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.ValidationFailure, (HttpStatusCode)422)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.Throttled, (HttpStatusCode)429)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData(PosServerSalesInvoiceProfileAdminOutcome.MalformedResponse, HttpStatusCode.BadGateway)]
    public async Task ManagementPlatformSalesInvoiceProfileApi_DownstreamOutcomesMapToSafeHttpErrors(
        PosServerSalesInvoiceProfileAdminOutcome outcome,
        HttpStatusCode expectedStatus)
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService { ForcedOutcome = outcome };
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.read");

        using var response = await client.GetAsync($"/v1/management-platform/fiscal-identities/{FiscalIdentityId:D}");

        response.StatusCode.Should().Be(expectedStatus);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.Message.Should().NotContain("server-side-placeholder-api-key");
        error.Message.Should().NotContain("https://pos-server.internal");
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_DisabledIntegrationReturnsSafeUnavailableResponse()
    {
        var fake = new FakeSalesInvoiceProfileAdministrationService { ForcedOutcome = PosServerSalesInvoiceProfileAdminOutcome.Disabled };
        using var factory = CreateFactory(fake);
        using var client = CreateAuthorizedClient(factory, "sales-invoice-profile.read");

        using var response = await client.GetAsync($"/v1/management-platform/fiscal-identities/{FiscalIdentityId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.ErrorCode.Should().Be("SALES_INVOICE_PROFILE_ADMINISTRATION_DISABLED");
        error.Message.Should().NotContain("server-side-placeholder-api-key");
        error.Message.Should().NotContain("https://pos-server.internal");
    }

    [Fact]
    public async Task ManagementPlatformSalesInvoiceProfileApi_RoutesExposeExpectedPoliciesAndNoProxyRoutes()
    {
        using var factory = CreateFactory(new FakeSalesInvoiceProfileAdministrationService());
        var endpoints = factory.Services
            .GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("/management-platform/", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == "/v1/management-platform/fiscal-identities/");
        endpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == "/v1/management-platform/sales-invoice-header-profiles/effective-readiness");
        endpoints.Any(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty).Contains("/v1/admin/", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeFalse();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[] { "SalesInvoiceProfileRead", "SalesInvoiceProfileManage", "SalesInvoiceProfileApprove" });
    }

    [Fact]
    public void ManagementPlatformSalesInvoiceProfileApi_BrowserDtosExposeNoPosServerSecretOrTerminalIdFields()
    {
        var dtoTypes = typeof(ManagementPlatformFiscalIdentityMutationRequestDto).Assembly.GetTypes()
            .Where(type => type.Namespace == "ExitPass.CentralPms.Contracts.ManagementPlatform" &&
                           (type.Name.Contains("SalesInvoice", StringComparison.OrdinalIgnoreCase) ||
                            type.Name.Contains("FiscalIdentity", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        dtoTypes.SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .Should()
            .NotContain(name =>
                name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("AdminKey", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("BaseUrl", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TerminalId", StringComparison.OrdinalIgnoreCase));
    }

    private static CustomWebApplicationFactory CreateFactory(FakeSalesInvoiceProfileAdministrationService fake)
    {
        return new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<ISalesInvoiceProfileAdministrationService>();
                services.AddSingleton<ISalesInvoiceProfileAdministrationService>(fake);
            });
    }

    private static HttpClient CreateAuthorizedClient(
        CustomWebApplicationFactory factory,
        string permission,
        Guid? authorizedSiteId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString("D"));
        if (authorizedSiteId is { } siteId)
        {
            client.DefaultRequestHeaders.Add("X-Site-Id", siteId.ToString("D"));
        }

        return client;
    }

    private static ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto CreateProfileRequest() =>
        new(
            FiscalIdentityId,
            SiteId,
            SitePosServerId,
            1,
            "template-v1",
            "presentation-v1",
            "POS-SAFE-001",
            "MIN-SAFE-001",
            "Safe Parking Site",
            "BIR-SAFE-001",
            new DateOnly(2026, 1, 1),
            new DateOnly(2031, 1, 1),
            "PTU-SAFE-001",
            new DateOnly(2026, 1, 1),
            "This document is valid for local tax reporting.",
            "Customer service footer",
            DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
            null,
            "Governed Test Software Supplier Inc.",
            "456 Software Park, Cebu City",
            "987-654-321-000");

    private sealed class FakeSalesInvoiceProfileAdministrationService : ISalesInvoiceProfileAdministrationService
    {
        public PosServerSalesInvoiceProfileAdminOutcome? ForcedOutcome { get; set; }
        public string ReadinessStatus { get; set; } = ManagementPlatformSalesInvoiceProfileReadinessStatuses.Ready;
        public Guid ProfileSiteId { get; set; } = SiteId;
        public int TotalCalls { get; private set; }
        public int ApproveCalls { get; private set; }
        public ManagementPlatformSalesInvoiceHeaderProfileMutationRequest? LastProfileMutationRequest { get; private set; }
        public ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest? LastApprovalRequest { get; private set; }
        public ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest? LastRetirementRequest { get; private set; }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
            ManagementPlatformFiscalIdentityMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(context, FiscalIdentity(request.RequestedByRef)));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
            Guid fiscalIdentityId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(context, FiscalIdentity("central-pms-user:creator")));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
            Guid fiscalIdentityId,
            ManagementPlatformFiscalIdentityMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(context, FiscalIdentity(request.RequestedByRef)));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
            ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastProfileMutationRequest = request;
            return Task.FromResult(SuccessOrFailure(context, Profile(request.SiteId, request.SitePosServerId, request.RequestedByRef)));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(context, Profile(ProfileSiteId, SitePosServerId, "central-pms-user:creator")));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
            ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile> profiles =
            [
                Profile(request.SiteId ?? SiteId, request.SitePosServerId ?? SitePosServerId, "central-pms-user:creator")
            ];

            return Task.FromResult(SuccessOrFailure(context, profiles));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastProfileMutationRequest = request;
            return Task.FromResult(SuccessOrFailure(context, Profile(request.SiteId, request.SitePosServerId, request.RequestedByRef)));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(
                context,
                new ManagementPlatformSalesInvoiceHeaderProfileValidation(
                    ProfileId,
                    ManagementPlatformSalesInvoiceProfileLifecycleStates.Draft,
                    false,
                    ["birAccreditationNumber", "futureSafeCode"],
                    ["Safe validation message."],
                    "SUPPORTED",
                    "SUPPORTED",
                    "VALID",
                    "NO_OVERLAP",
                    "VALID",
                    DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                    context.GetOrCreateCorrelationId())));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            ApproveCalls++;
            LastApprovalRequest = request;
            return Task.FromResult(SuccessOrFailure(context, Profile(SiteId, SitePosServerId, request.ApprovedByRef) with
            {
                LifecycleState = ManagementPlatformSalesInvoiceProfileLifecycleStates.Approved,
                ApprovedByRef = request.ApprovedByRef,
                ApprovedAt = DateTimeOffset.Parse("2026-07-19T01:00:00Z")
            }));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            LastRetirementRequest = request;
            return Task.FromResult(SuccessOrFailure(context, Profile(SiteId, SitePosServerId, request.RetiredByRef) with
            {
                LifecycleState = ManagementPlatformSalesInvoiceProfileLifecycleStates.Retired,
                RetiredAt = request.RetireAt
            }));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
            ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(
                context,
                new ManagementPlatformSalesInvoiceHeaderProfileReadiness(
                    request.SiteId,
                    request.SitePosServerId,
                    request.EffectiveAt,
                    ReadinessStatus,
                    ProfileId,
                    1,
                    FiscalIdentityId,
                    ManagementPlatformSalesInvoiceProfileLifecycleStates.Approved,
                    true,
                    true,
                    [],
                    "VALID",
                    "COMPLETE",
                    "SUPPORTED",
                    "NO_OVERLAP",
                    DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                    context.GetOrCreateCorrelationId())));
        }

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(SuccessOrFailure(
                context,
                new ManagementPlatformSalesInvoiceHeaderProfileUsage(
                    ProfileId,
                    1,
                    FiscalIdentityId,
                    DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                    DateTimeOffset.Parse("2026-07-19T01:00:00Z"),
                    1,
                    ["DSI-SAFE-0001"],
                    true,
                    context.GetOrCreateCorrelationId())));
        }

        private PosServerSalesInvoiceProfileAdminResult<T> SuccessOrFailure<T>(
            ManagementPlatformPosServerAdminRequestContext context,
            T value)
        {
            var correlationId = context.GetOrCreateCorrelationId();
            return ForcedOutcome is { } outcome && outcome != PosServerSalesInvoiceProfileAdminOutcome.Succeeded
                ? PosServerSalesInvoiceProfileAdminResult<T>.Failure(
                    outcome,
                    $"safe_{outcome.ToString().ToLowerInvariant()}",
                    "Safe downstream outcome.",
                    correlationId)
                : PosServerSalesInvoiceProfileAdminResult<T>.Success(value, correlationId, StatusCodes.Status200OK);
        }

        private static ManagementPlatformFiscalIdentity FiscalIdentity(string actorRef) =>
            new(
                FiscalIdentityId,
                "Safe Business Name",
                "Safe registered address",
                "TIN-SAFE-001",
                "VAT_REGISTERED",
                "ACTIVE",
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                null,
                actorRef,
                null);

        private static ManagementPlatformSalesInvoiceHeaderProfile Profile(
            Guid siteId,
            Guid sitePosServerId,
            string actorRef) =>
            new(
                ProfileId,
                FiscalIdentityId,
                siteId,
                sitePosServerId,
                1,
                "template-v1",
                "presentation-v1",
                "POS-SAFE-001",
                "MIN-SAFE-001",
                "Safe Parking Site",
                "BIR-SAFE-001",
                new DateOnly(2026, 1, 1),
                new DateOnly(2031, 1, 1),
                "PTU-SAFE-001",
                new DateOnly(2026, 1, 1),
                "This document is valid for local tax reporting.",
                "Customer service footer",
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                null,
                ManagementPlatformSalesInvoiceProfileLifecycleStates.Draft,
                null,
                null,
                null,
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                null,
                "Governed Test Software Supplier Inc.",
                "456 Software Park, Cebu City",
                "987-654-321-000");
    }
}
