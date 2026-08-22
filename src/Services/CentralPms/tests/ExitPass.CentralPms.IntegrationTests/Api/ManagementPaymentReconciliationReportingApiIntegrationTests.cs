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

public sealed class ManagementPaymentReconciliationReportingApiIntegrationTests
{
    private const string Path = "/v1/management-platform/dashboard/payment-reconciliation-summary";
    private static readonly Guid UserId = Guid.Parse("93400000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("93400000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("93400000-0000-0000-0000-000000000101");
    private static readonly Guid CorrelationId = Guid.Parse("93400000-0000-0000-0000-000000000301");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
    private static readonly DateTimeOffset End = DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public async Task Summary_WhenAuthorized_ReturnsVersionedReadOnlyContractAndBoundQuery()
    {
        var service = new FakeService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementPaymentReconciliationReportingValues.Permission);

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ManagementPaymentReconciliationSummaryResponse>();
        body!.ContractVersion.Should().Be(ManagementPaymentReconciliationReportingValues.ContractVersion);
        body.ReportId.Should().Be(ManagementPaymentReconciliationReportingValues.ReportId);
        body.RequestedScope.ScopeReference.Should().Be(SiteId);
        body.PeriodStart.Should().Be(Start);
        body.PeriodEnd.Should().Be(End);
        body.SourceAuthority.Should().Be(ManagementPaymentReconciliationReportingValues.SourceAuthority);
        body.CurrencySummaries.Should().ContainSingle(summary => summary.CurrencyCode == "PHP" && summary.ConfirmedAmount == 100.10m);
        service.Actor.Should().Be(new ManagementDashboardActor(UserId, SessionId));
        service.Query.Should().Be(new ManagementPaymentReconciliationQuery(
            "SITE",
            SiteId,
            "2026-08-01T00:00:00Z",
            "2026-08-02T00:00:00Z",
            CorrelationId));
    }

    [Fact]
    public async Task Summary_WhenUnauthenticated_Returns401()
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode
            .Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
    }

    [Fact]
    public async Task Summary_WhenPermissionMissing_Returns403WithoutCallingService()
    {
        var service = new FakeService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementDashboardReportingValues.OverviewPermission);

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.Query.Should().BeNull();
    }

    [Fact]
    public async Task Summary_DoesNotTrustClientAuthoredManagementAuthorityHeaders()
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Management-Platform-User-Id", UserId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Management-Platform-Permissions", ManagementPaymentReconciliationReportingValues.Permission);
        client.DefaultRequestHeaders.Add("X-Management-Platform-Site-Id", SiteId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Authorization-Epoch", "999");

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(ManagementPaymentReconciliationOutcome.InvalidPeriod, HttpStatusCode.BadRequest, ManagementPaymentReconciliationReportingValues.InvalidPeriodRange)]
    [InlineData(ManagementPaymentReconciliationOutcome.InvalidScope, HttpStatusCode.BadRequest, ManagementDashboardReportingValues.InvalidScopeType)]
    [InlineData(ManagementPaymentReconciliationOutcome.ScopeNotFoundOrDenied, HttpStatusCode.NotFound, ManagementDashboardReportingValues.ScopeNotFoundOrDenied)]
    [InlineData(ManagementPaymentReconciliationOutcome.FeatureDisabled, HttpStatusCode.ServiceUnavailable, ManagementPaymentReconciliationReportingValues.FeatureDisabled)]
    [InlineData(ManagementPaymentReconciliationOutcome.SourceUnavailable, HttpStatusCode.ServiceUnavailable, ManagementPaymentReconciliationReportingValues.SourceUnavailable)]
    public async Task Summary_MapsControlledFailures(
        ManagementPaymentReconciliationOutcome outcome,
        HttpStatusCode expectedStatus,
        string errorCode)
    {
        var service = new FakeService
        {
            Result = ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Failed(
                outcome,
                CorrelationId,
                errorCode,
                "Safe controlled failure.",
                outcome == ManagementPaymentReconciliationOutcome.SourceUnavailable)
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementPaymentReconciliationReportingValues.Permission);

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(expectedStatus);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(errorCode);
        body.Message.Should().Be("Safe controlled failure.");
    }

    [Fact]
    public async Task Summary_EndpointIsGetOnlyAndUsesDedicatedPolicy()
    {
        using var factory = CreateFactory(new FakeService());
        using var scope = factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Path)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().Equal("GET");
        endpoints[0].Metadata.GetMetadata<ReconciliationPolicyMetadata>()!.PolicyName
            .Should().Be(ManagementPaymentReconciliationReportingValues.Policy);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPaymentReconciliationReportingValues.Policy)
            .Should().Equal(ManagementPaymentReconciliationReportingValues.Permission);

        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementPaymentReconciliationReportingValues.Permission);
        using var post = await client.PostAsync(QueryPath(), null);
        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Summary_ResponseDoesNotExposeSensitiveOrTransactionLevelFields()
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementPaymentReconciliationReportingValues.Permission);

        using var response = await client.GetAsync(QueryPath());
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = EnumeratePropertyNames(json.RootElement).ToArray();

        names.Should().NotContain(name =>
            name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("plate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("payer", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("transactionReference", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("paymentAttemptId", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("paymentConfirmationId", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("providerPayload", StringComparison.OrdinalIgnoreCase));
    }

    private static CustomWebApplicationFactory CreateFactory(FakeService service) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true",
                ["ManagementPlatform:DashboardReporting:Enabled"] = "true",
                ["ManagementPlatform:DashboardReporting:PaymentReconciliation:Enabled"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IIdentityAdministrationActorAccessor>();
                services.AddSingleton<IIdentityAdministrationActorAccessor>(
                    new FakeActorAccessor(new IdentityAdministrationActor(UserId, SessionId)));
                services.RemoveAll<IManagementPaymentReconciliationReportingService>();
                services.AddSingleton<IManagementPaymentReconciliationReportingService>(service);
            });

    private static string QueryPath() =>
        $"{Path}?scopeType=SITE&scopeReference={SiteId:D}&periodStart=2026-08-01T00%3A00%3A00Z&periodEnd=2026-08-02T00%3A00%3A00Z";

    private static void AddFixtureAuthorization(HttpClient client, string permission)
    {
        client.DefaultRequestHeaders.Add("X-Correlation-Id", CorrelationId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, UserId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, permission);
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

    private sealed class FakeActorAccessor(IdentityAdministrationActor current) : IIdentityAdministrationActorAccessor
    {
        public IdentityAdministrationActor? Current { get; } = current;
    }

    private sealed class FakeService : IManagementPaymentReconciliationReportingService
    {
        public ManagementDashboardActor? Actor { get; private set; }
        public ManagementPaymentReconciliationQuery? Query { get; private set; }
        public ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>? Result { get; init; }

        public Task<ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>> GetSummaryAsync(
            ManagementDashboardActor actor,
            ManagementPaymentReconciliationQuery query,
            CancellationToken cancellationToken)
        {
            Actor = actor;
            Query = query;
            return Task.FromResult(Result ?? ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport>.Success(
                Report(query.CorrelationId),
                query.CorrelationId));
        }

        private static ManagementPaymentReconciliationReport Report(Guid correlationId)
        {
            var scope = new ManagementDashboardScope("SITE", SiteId, "Synthetic Site");
            return new ManagementPaymentReconciliationReport(
                ManagementPaymentReconciliationReportingValues.ContractVersion,
                ManagementPaymentReconciliationReportingValues.ReportId,
                scope,
                scope,
                Start,
                End,
                End,
                End.AddMinutes(-1),
                ManagementDashboardReportingValues.Partial,
                ManagementDashboardReportingValues.Current,
                correlationId,
                [new ManagementPaymentCurrencySummary("PHP", 1, 100.10m, 1, 100.10m)],
                [new ManagementPaymentStatusSummary("CONFIRMED", "PHP", 1, 100.10m)],
                [new ManagementPaymentStatusSummary("RECORDED", "PHP", 1, 100.10m)],
                [new ManagementPaymentCanonicalStatusSummary("PAYMENT_ATTEMPT", "CONFIRMED", "PHP", 1, 100.10m)],
                [new ManagementPaymentChannelSummary("QRPH", "QRPH", "PHP", 1, 100.10m, 1, 100.10m)],
                [new ManagementPaymentProviderSummary("PAYMONGO", "PHP", 1, 100.10m, 1, 100.10m, 1, 100.10m)],
                [new ManagementInternalReconciliationSummary("AMOUNT_MISMATCH", "AVAILABLE", 0, [], "Synthetic.", "No amount.", [])],
                [],
                ["No settlement claim."],
                ManagementPaymentReconciliationReportingValues.SourceAuthority);
        }
    }
}
