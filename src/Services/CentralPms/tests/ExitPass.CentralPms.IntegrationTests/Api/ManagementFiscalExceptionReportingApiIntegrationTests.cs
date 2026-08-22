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

public sealed class ManagementFiscalExceptionReportingApiIntegrationTests
{
    private const string Path = "/v1/management-platform/dashboard/fiscal-exception-summary";
    private static readonly Guid UserId = Guid.Parse("93600000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("93600000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("93600000-0000-0000-0000-000000000101");
    private static readonly Guid CorrelationId = Guid.Parse("93600000-0000-0000-0000-000000000301");

    [Fact]
    public async Task Summary_WhenAuthorized_ReturnsVersionedAggregateContractAndBoundQuery()
    {
        var service = new FakeService();
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementFiscalExceptionReportingValues.Permission);

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ManagementFiscalExceptionSummaryResponse>();
        body!.ContractVersion.Should().Be(ManagementFiscalExceptionReportingValues.ContractVersion);
        body.ReportId.Should().Be(ManagementFiscalExceptionReportingValues.ReportId);
        body.TimeBasis.Should().Be(ManagementFiscalExceptionReportingValues.TimeBasis);
        body.SourceAuthority.Should().Be(ManagementFiscalExceptionReportingValues.SourceAuthority);
        body.CurrencySummaries.Should().ContainSingle(summary =>
            summary.CurrencyCode == "PHP" && summary.ExpectedIssuanceAmount == 100.10m);
        service.Actor.Should().Be(new ManagementDashboardActor(UserId, SessionId));
        service.Query!.ScopeReference.Should().Be(SiteId);
    }

    [Fact]
    public async Task Summary_UnauthenticatedAndMissingPermissionFailBeforeService()
    {
        var service = new FakeService();
        using var factory = CreateFactory(service);
        using var anonymous = factory.CreateClient();
        using var denied = factory.CreateClient();
        AddFixtureAuthorization(denied, ManagementDashboardReportingValues.OverviewPermission);

        (await anonymous.GetAsync(QueryPath())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await denied.GetAsync(QueryPath())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.Query.Should().BeNull();
    }

    [Fact]
    public async Task Summary_DoesNotTrustClientAuthoredManagementAuthorityHeaders()
    {
        using var factory = CreateFactory(new FakeService());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Management-Platform-User-Id", UserId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Management-Platform-Permissions", ManagementFiscalExceptionReportingValues.Permission);
        client.DefaultRequestHeaders.Add("X-Management-Platform-Site-Id", SiteId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Authorization-Epoch", "999");

        (await client.GetAsync(QueryPath())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(ManagementFiscalExceptionOutcome.InvalidPeriod, HttpStatusCode.BadRequest, ManagementFiscalExceptionReportingValues.InvalidPeriodRange)]
    [InlineData(ManagementFiscalExceptionOutcome.InvalidScope, HttpStatusCode.BadRequest, ManagementDashboardReportingValues.InvalidScopeType)]
    [InlineData(ManagementFiscalExceptionOutcome.ScopeNotFoundOrDenied, HttpStatusCode.NotFound, ManagementDashboardReportingValues.ScopeNotFoundOrDenied)]
    [InlineData(ManagementFiscalExceptionOutcome.FeatureDisabled, HttpStatusCode.ServiceUnavailable, ManagementFiscalExceptionReportingValues.FeatureDisabled)]
    [InlineData(ManagementFiscalExceptionOutcome.SourceUnavailable, HttpStatusCode.ServiceUnavailable, ManagementFiscalExceptionReportingValues.SourceUnavailable)]
    public async Task Summary_MapsControlledFailures(
        ManagementFiscalExceptionOutcome outcome,
        HttpStatusCode expectedStatus,
        string errorCode)
    {
        var service = new FakeService
        {
            Result = ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Failed(
                outcome, CorrelationId, errorCode, "Safe controlled failure.",
                outcome == ManagementFiscalExceptionOutcome.SourceUnavailable)
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementFiscalExceptionReportingValues.Permission);

        using var response = await client.GetAsync(QueryPath());

        response.StatusCode.Should().Be(expectedStatus);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(errorCode);
        body.Message.Should().Be("Safe controlled failure.");
    }

    [Fact]
    public async Task Summary_IsGetOnlyAndResponseContainsNoSensitiveFields()
    {
        using var factory = CreateFactory(new FakeService());
        using var scope = factory.Services.CreateScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(item => item.RoutePattern.RawText == Path);
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().Equal("GET");
        endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()!.PolicyName
            .Should().Be(ManagementFiscalExceptionReportingValues.Policy);

        using var client = factory.CreateClient();
        AddFixtureAuthorization(client, ManagementFiscalExceptionReportingValues.Permission);
        (await client.PostAsync(QueryPath(), null)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var json = JsonDocument.Parse(await (await client.GetAsync(QueryPath())).Content.ReadAsStringAsync());
        EnumeratePropertyNames(json.RootElement).Should().NotContain(name =>
            name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("plate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("payer", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ticket", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("fiscalDocumentNumber", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("providerReference", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("paymentConfirmationId", StringComparison.OrdinalIgnoreCase));
    }

    private static CustomWebApplicationFactory CreateFactory(FakeService service) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true",
                ["ManagementPlatform:DashboardReporting:Enabled"] = "true",
                ["ManagementPlatform:DashboardReporting:FiscalExceptions:Enabled"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IIdentityAdministrationActorAccessor>();
                services.AddSingleton<IIdentityAdministrationActorAccessor>(
                    new FakeActorAccessor(new IdentityAdministrationActor(UserId, SessionId)));
                services.RemoveAll<IManagementFiscalExceptionReportingService>();
                services.AddSingleton<IManagementFiscalExceptionReportingService>(service);
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
                foreach (var nested in EnumeratePropertyNames(property.Value)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in EnumeratePropertyNames(item)) yield return nested;
        }
    }

    private sealed class FakeActorAccessor(IdentityAdministrationActor current) : IIdentityAdministrationActorAccessor
    {
        public IdentityAdministrationActor? Current { get; } = current;
    }

    private sealed class FakeService : IManagementFiscalExceptionReportingService
    {
        public ManagementDashboardActor? Actor { get; private set; }
        public ManagementFiscalExceptionQuery? Query { get; private set; }
        public ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>? Result { get; init; }

        public Task<ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>> GetSummaryAsync(
            ManagementDashboardActor actor,
            ManagementFiscalExceptionQuery query,
            CancellationToken cancellationToken)
        {
            Actor = actor;
            Query = query;
            return Task.FromResult(Result ?? ManagementFiscalExceptionResult<ManagementFiscalExceptionReport>.Success(
                Report(query.CorrelationId), query.CorrelationId));
        }

        private static ManagementFiscalExceptionReport Report(Guid correlationId)
        {
            var scope = new ManagementDashboardScope("SITE", SiteId, "Synthetic Site");
            var start = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
            var end = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
            return new ManagementFiscalExceptionReport(
                ManagementFiscalExceptionReportingValues.ContractVersion,
                ManagementFiscalExceptionReportingValues.ReportId,
                scope, scope, start, end, ManagementFiscalExceptionReportingValues.TimeBasis,
                end, end.AddMinutes(-1), ManagementDashboardReportingValues.Partial,
                ManagementDashboardReportingValues.Current, correlationId,
                [new ManagementFiscalSourceCoverage("central-pms-fiscal-issuance-references", "AVAILABLE", end, "Synthetic.", [])],
                [new ManagementFiscalLifecycleSummary("ISSUED", 1)],
                [new ManagementFiscalExceptionSummary(ManagementFiscalExceptionReportingValues.IssuanceFailed, "AVAILABLE", 0, [], "Synthetic.", false, true, [])],
                [new ManagementFiscalCurrencySummary("PHP", 1, 100.10m, 1, 0)],
                [], ["No print claim."], ["PRINT_RESULT_UNAVAILABLE"],
                ManagementFiscalExceptionReportingValues.SourceAuthority);
        }
    }
}
