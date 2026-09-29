using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleFiscalReportingApiIntegrationTests
{
    private const string Scheme = "FiscalReportingTest";
    private static readonly Guid UserId = Guid.Parse("88000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("88000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherSiteId = Guid.Parse("88000000-0000-0000-0000-000000000003");
    private const string Ticket = "MOCK-USABILITY-20260924-01";
    private const string Plate = "USR2401";
    private const string SalesInvoice = "SI-00000049";

    [Theory]
    [InlineData(Ticket)]
    [InlineData(Plate)]
    [InlineData(SalesInvoice)]
    public async Task ElectronicJournal_ExactOperatorIdentifier_ResolvesToAuthoritativeSalesInvoice(string identifier)
    {
        var gateway = new FakeGateway();
        var references = new FakeReferenceRepository();
        using var factory = CreateFactory(gateway, references, role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, identifier));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        gateway.CallCount.Should().Be(1);
        gateway.LastRequest!.Search.Should().Be(SalesInvoice);
        references.LastIdentifier.Should().Be(identifier);
        references.LastSiteId.Should().Be(SiteId);
    }

    [Fact]
    public async Task ElectronicJournal_UnknownIdentifier_ReturnsControlledEmptyResultWithoutCallingPos()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, "UNKNOWN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"invoices\":");
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_AmbiguousIdentifier_ReturnsConflictWithoutCallingPos()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, "AMBIGUOUS"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fiscal_reporting_identifier_ambiguous");
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_SiteOperatorWithStalePermission_IsDenied()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "SITE_OPERATOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("OPERATOR_CONSOLE_FISCAL_REPORTING_ROLE_REQUIRED");
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DigitalSalesInvoice_SiteOperatorWithStatusPermission_IsRoleDenied()
    {
        using var factory = CreateFactory(
            new FakeGateway(),
            new FakeReferenceRepository(),
            role: "SITE_OPERATOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/v1/ops/operator-console/fiscal-issuance/references/{Guid.NewGuid():D}/digital-sales-invoice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain("OPERATOR_CONSOLE_FISCAL_REPORTING_ROLE_REQUIRED");
    }

    [Fact]
    public async Task ElectronicJournal_OutsideAuthenticatedSiteScope_IsConcealed()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(OtherSiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_MissingPermission_IsDenied()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(
            gateway,
            new FakeReferenceRepository(),
            role: "OPERATIONS_SUPERVISOR",
            includePermission: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ElectronicJournal_Unauthenticated_IsDenied()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(
            gateway,
            new FakeReferenceRepository(),
            role: "OPERATIONS_SUPERVISOR",
            authenticated: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EjUrl(SiteId, Ticket));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CloseablePeriods_UsesExactlyTheAuthenticatedSiteScope()
    {
        var gateway = new FakeGateway(request => Response(200, CloseablePeriodsJson(
            Period("2026-09-20", 10),
            Period("2026-09-21", 11))));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        gateway.Requests.Should().ContainSingle();
        gateway.Requests[0].SiteId.Should().Be(SiteId);
        gateway.Requests[0].Action.Should().Be(FiscalReportingGatewayAction.ZCloseablePeriods);
    }

    [Fact]
    public async Task CloseOne_UsesThePosPeriodAndExpectedVersionWithoutEnumeratingAnotherSite()
    {
        var periodId = Guid.Parse("88000000-0000-0000-0000-000000000021");
        var gateway = new FakeGateway(_ => Response(201, "{\"succeeded\":true}"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/{periodId:D}/close",
            new { operationKey = "close-one", expectedStateVersion = 23, confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = gateway.Requests.Should().ContainSingle().Subject;
        request.SiteId.Should().Be(SiteId);
        request.Action.Should().Be(FiscalReportingGatewayAction.ZClosePeriod);
        request.FiscalReportingPeriodId.Should().Be(periodId);
        request.ExpectedStateVersion.Should().Be(23);
        gateway.Requests.Should().NotContain(item => item.SiteId == OtherSiteId);
    }

    [Fact]
    public async Task CloseOne_PlainTextUpstreamException_ReturnsControlledJsonWithoutDiagnostics()
    {
        var periodId = Guid.Parse("88000000-0000-0000-0000-000000000022");
        var gateway = new FakeGateway(_ => new FiscalReportingGatewayResponse(
            500,
            "text/plain",
            Encoding.UTF8.GetBytes("ExitPass.PosServer.Runtime.InternalException: private stack trace"),
            "upstream_failure"));
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/{periodId:D}/close",
            new { operationKey = "controlled-error", expectedStateVersion = 7, confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("fiscal_reporting_upstream_operation_failed");
        body.Should().Contain("The authoritative fiscal reporting operation could not be completed.");
        body.Should().NotContain("ExitPass.PosServer");
        body.Should().NotContain("stack trace");
    }

    [Fact]
    public async Task CloseAll_ProcessesOldestFirstAndStopsAtTheFirstFailure()
    {
        var periods = new[]
        {
            Period("2026-09-20", 10),
            Period("2026-09-21", 11),
            Period("2026-09-22", 12)
        };
        var closeCalls = 0;
        var gateway = new FakeGateway(request => request.Action switch
        {
            FiscalReportingGatewayAction.ZCloseablePeriods => Response(200, CloseablePeriodsJson(periods)),
            FiscalReportingGatewayAction.ZClosePeriod when ++closeCalls == 1 => Response(201, "{\"succeeded\":true}"),
            FiscalReportingGatewayAction.ZClosePeriod => Response(409, "{\"succeeded\":false,\"code\":\"stale_state_version\"}"),
            _ => throw new InvalidOperationException("Unexpected gateway action.")
        });
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{SiteId:D}/z-readings/closeable-periods/close-all",
            new { operationKey = "close-all", confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"closedCount\":1");
        body.Should().Contain("2026-09-21");
        gateway.Requests.Select(item => item.Action).Should().Equal(
            FiscalReportingGatewayAction.ZCloseablePeriods,
            FiscalReportingGatewayAction.ZClosePeriod,
            FiscalReportingGatewayAction.ZClosePeriod,
            FiscalReportingGatewayAction.ZCloseablePeriods);
        gateway.Requests.Where(item => item.Action == FiscalReportingGatewayAction.ZClosePeriod)
            .Select(item => item.FiscalReportingPeriodId)
            .Should().Equal(periods[0].FiscalReportingPeriodId, periods[1].FiscalReportingPeriodId);
        gateway.Requests.Should().OnlyContain(item => item.SiteId == SiteId);
    }

    [Fact]
    public async Task CloseAll_OutsideAuthenticatedSiteScope_IsConcealedBeforePosIsCalled()
    {
        var gateway = new FakeGateway();
        using var factory = CreateFactory(gateway, new FakeReferenceRepository(), role: "OPERATIONS_SUPERVISOR");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/v1/ops/operator-console/fiscal-reporting/sites/{OtherSiteId:D}/z-readings/closeable-periods/close-all",
            new { operationKey = "wrong-site", confirmClose = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        gateway.Requests.Should().BeEmpty();
    }

    private static string EjUrl(Guid siteId, string identifier) =>
        $"/v1/ops/operator-console/fiscal-reporting/sites/{siteId:D}/electronic-journal" +
        $"?periodStart=2026-09-23T00%3A00%3A00Z&periodEnd=2026-09-25T00%3A00%3A00Z&search={Uri.EscapeDataString(identifier)}";

    private static CloseableTestPeriod Period(string businessDate, long version) =>
        new(Guid.Parse($"88000000-0000-0000-0000-{version:000000000000}"), businessDate, version);

    private static string CloseablePeriodsJson(params CloseableTestPeriod[] periods) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            succeeded = true,
            fiscalBusinessDates = periods.Select(period => new
            {
                fiscalReportingPeriodId = period.FiscalReportingPeriodId,
                fiscalBusinessDate = period.FiscalBusinessDate,
                periodStart = $"{period.FiscalBusinessDate}T00:00:00Z",
                periodEnd = $"{period.FiscalBusinessDate}T23:59:59Z",
                status = "CLOSEABLE",
                transactionCount = 1,
                expectedStateVersion = period.Version
            })
        });

    private static FiscalReportingGatewayResponse Response(int status, string body) =>
        new(status, "application/json", Encoding.UTF8.GetBytes(body), status < 400 ? "ok" : "failed");

    private static CustomWebApplicationFactory CreateFactory(
        FakeGateway gateway,
        FakeReferenceRepository references,
        string role,
        bool includePermission = true,
        bool authenticated = true) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowFixtureIdentityHeaders"] = "false"
            })
            .WithServiceOverrides(services =>
            {
                services.AddSingleton(new TestPrincipalOptions(role, includePermission, authenticated));
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = Scheme;
                        options.DefaultChallengeScheme = Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(Scheme, _ => { });
                services.RemoveAll<IFiscalReportingPosServerGateway>();
                services.AddSingleton<IFiscalReportingPosServerGateway>(gateway);
                services.RemoveAll<IFiscalIssuanceReferenceRepository>();
                services.AddSingleton<IFiscalIssuanceReferenceRepository>(references);
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository(role));
            });

    private sealed record TestPrincipalOptions(string Role, bool IncludePermission, bool Authenticated);

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly TestPrincipalOptions _principal;

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TestPrincipalOptions principal)
            : base(options, logger, encoder)
        {
            _principal = principal;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!_principal.Authenticated)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, UserId.ToString("D")),
                new(ClaimTypes.Role, _principal.Role),
                new("exitpass_audience", "OPERATOR_CONSOLE"),
                new("site_id", SiteId.ToString("D"))
            };
            if (_principal.IncludePermission)
            {
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.ej.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-issuance.status.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.z.read"));
                claims.Add(new(CentralPmsRbacPolicyCatalog.PermissionClaimType, "fiscal-reporting.z.generate"));
            }

            var identity = new ClaimsIdentity(claims, OperatorConsoleFiscalReportingApiIntegrationTests.Scheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity),
                    OperatorConsoleFiscalReportingApiIntegrationTests.Scheme)));
        }
    }

    private sealed class FakeGateway(Func<FiscalReportingGatewayRequest, FiscalReportingGatewayResponse>? response = null) : IFiscalReportingPosServerGateway
    {
        public List<FiscalReportingGatewayRequest> Requests { get; } = [];
        public int CallCount => Requests.Count;
        public FiscalReportingGatewayRequest? LastRequest => Requests.LastOrDefault();

        public Task<FiscalReportingGatewayResponse> SendAsync(
            FiscalReportingGatewayRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(response?.Invoke(request) ?? new FiscalReportingGatewayResponse(
                200,
                "application/json",
                Encoding.UTF8.GetBytes("{\"invoices\":[]}"),
                "ok"));
        }
    }

    private sealed class FakeReferenceRepository : IFiscalIssuanceReferenceRepository
    {
        public string? LastIdentifier { get; private set; }
        public Guid? LastSiteId { get; private set; }

        public Task<IReadOnlyList<FiscalIssuanceOperatorLookupMatch>> FindByOperatorIdentifierAsync(
            string identifier,
            Guid? siteId,
            CancellationToken cancellationToken)
        {
            LastIdentifier = identifier;
            LastSiteId = siteId;
            if (identifier == "UNKNOWN")
            {
                return Task.FromResult<IReadOnlyList<FiscalIssuanceOperatorLookupMatch>>([]);
            }

            var match = new FiscalIssuanceOperatorLookupMatch(Reference(), Ticket, Plate);
            return Task.FromResult<IReadOnlyList<FiscalIssuanceOperatorLookupMatch>>(
                identifier == "AMBIGUOUS" ? [match, match] : [match]);
        }

        public Task<FiscalIssuanceReferenceRecord> CreateAsync(CreateFiscalIssuanceReferenceRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FiscalIssuanceReferenceRecord> UpdateStateAsync(Guid fiscalIssuanceReferenceId, FiscalIssuanceStateTransitionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FiscalIssuanceReferenceRecord> RecordSemanticRequestHashAsync(Guid fiscalIssuanceReferenceId, FiscalSemanticRequestHashResult semanticRequestHash, Guid? serviceIdentityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<FiscalIssuanceReferenceRecord?> FindByFiscalIssuanceReferenceIdAsync(Guid fiscalIssuanceReferenceId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(Guid paymentConfirmationId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindLatestByPaymentAttemptIdAsync(Guid paymentAttemptId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(string upstreamFinalityReference, Guid? sitePosServerId, Guid? fiscalDocumentTypeCodeId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);
        public Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(Guid posServerFiscalDocumentId, CancellationToken cancellationToken) =>
            Task.FromResult<FiscalIssuanceReferenceRecord?>(null);

        private static FiscalIssuanceReferenceRecord Reference()
        {
            var now = DateTimeOffset.Parse("2026-09-24T10:00:00+08:00");
            return new FiscalIssuanceReferenceRecord(
                FiscalIssuanceReferenceId: Guid.Parse("88000000-0000-0000-0000-000000000010"),
                PaymentConfirmationId: Guid.NewGuid(),
                PaymentAttemptId: Guid.NewGuid(),
                ParkingSessionId: Guid.NewGuid(),
                TariffSnapshotId: Guid.NewGuid(),
                SiteId,
                SitePosServerId: Guid.NewGuid(),
                SitePosServerRef: "PITX-L3-WEBPAY-01",
                PayableBasisRef: "regular",
                UpstreamFinalityReference: "payment-finality",
                PosServerFiscalDocumentId: Guid.NewGuid(),
                FiscalIdentityId: Guid.NewGuid(),
                FiscalSequencePolicyId: Guid.NewGuid(),
                FiscalSequenceValue: 49,
                FiscalDocumentNumber: SalesInvoice,
                FiscalSeries: "SI",
                FiscalNumberPrefixText: "SI-",
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: now,
                FiscalNumberAssignedByRef: "pos-server",
                FiscalDocumentStatusCodeId: Guid.NewGuid(),
                ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                LatestExceptionReason: null,
                LatestErrorCode: null,
                LatestErrorPosture: null,
                CorrelationId: Guid.NewGuid(),
                PosServerResponseTimestamp: now,
                FirstRecordedAt: now,
                LastUpdatedAt: now,
                RecordedByServiceIdentityId: Guid.NewGuid());
        }
    }

    private sealed class FakeRbacRepository(string role) : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(Guid userId, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> UserHasAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken) =>
            Task.FromResult(roleCodes.Contains(role));
        public Task<bool> ServiceIdentityIsActiveAsync(Guid serviceIdentityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task RecordDeniedAsync(string policyName, Guid? userId, Guid? serviceIdentityId, Guid? correlationId, string requestPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task RecordAuditEventAsync(string eventType, string eventResult, string eventReasonCode, string targetEntityType, Guid? targetEntityId, Guid? actorUserId, Guid? actorServiceIdentityId, Guid? correlationId, string summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record CloseableTestPeriod(Guid FiscalReportingPeriodId, string FiscalBusinessDate, long Version);
}
