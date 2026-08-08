using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(AptStatutoryOrdinanceAvailabilityNoWriteCollection.Name)]
public sealed class AptStatutoryOrdinanceAvailabilityApiIntegrationTests
{
    private const string Route = "/v1/apt/statutory-discounts/ordinance-availability";
    private const string ReadPermission = "statutory-discounts.ordinance-availability.read.apt";
    private static readonly Guid SiteGroupId = Guid.Parse("92000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("92000000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("92000000-0000-0000-0000-000000000003");
    private static readonly Guid ServiceIdentityId = Guid.Parse("92000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task Resolve_WhenCovered_ReturnsAptSafeAvailabilityWithoutWorkflowWrites()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddAptHeaders(client, SiteId);
        var request = Request("SENIOR_CITIZEN");

        var before = await CaptureForbiddenWorkflowRowCountsAsync();
        using var response = await client.PostAsJsonAsync($"{Route}/resolve", request);
        var after = await CaptureForbiddenWorkflowRowCountsAsync();

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var body = await response.Content.ReadFromJsonAsync<AptStatutoryOrdinanceAvailabilityResponse>();
        body.Should().NotBeNull();
        body!.Classification.Should().Be(AptStatutoryOrdinanceAvailabilityValues.Available);
        body.StatutoryRequestAllowed.Should().BeTrue();
        body.OrdinaryPaymentPreserved.Should().BeTrue();
        body.PreCashRevalidationPassed.Should().BeFalse();
        after.Should().Equal(before);
    }

    [Fact]
    public async Task Revalidate_WhenCoverageFailed_ReturnsFailedAndPreservesOrdinaryPayment()
    {
        using var factory = CreateFactory(new FakeAvailabilityService(AptStatutoryOrdinanceAvailabilityValues.Expired));
        using var client = factory.CreateClient();
        AddAptHeaders(client, SiteId);
        var request = Request("PWD");

        var before = await CaptureForbiddenWorkflowRowCountsAsync();
        using var response = await client.PostAsJsonAsync($"{Route}/revalidate", request);
        var after = await CaptureForbiddenWorkflowRowCountsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AptStatutoryOrdinanceAvailabilityResponse>();
        body.Should().NotBeNull();
        body!.RevalidationOutcome.Should().Be(AptStatutoryOrdinanceAvailabilityValues.Failed);
        body.PreCashRevalidationPassed.Should().BeFalse();
        body.ReadyForStatutoryCashFlow.Should().BeFalse();
        body.OrdinaryPaymentPreserved.Should().BeTrue();
        after.Should().Equal(before);
    }

    [Fact]
    public async Task Resolve_WhenServiceIdentityMissing_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString("D"));

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be("CENTRAL_PMS_SERVICE_IDENTITY_REQUIRED");
    }

    [Fact]
    public async Task Resolve_WhenHumanUserOnly_ReturnsUnauthorized()
    {
        using var factory = CreateFactory(enableRbac: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, ReadPermission);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resolve_WhenPermissionMissing_ReturnsForbidden()
    {
        using var factory = CreateFactory(enableRbac: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Site-Id", SiteId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, ServiceIdentityId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, AptHumanPermissionCatalog.PayableBasisRead);

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resolve_WhenSiteHeaderDoesNotMatchRequest_ReturnsAccessDenied()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        AddAptHeaders(client, Guid.NewGuid());

        using var response = await client.PostAsJsonAsync($"{Route}/resolve", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.ErrorCode.Should().Be(AptStatutoryOrdinanceAvailabilityValues.AccessDenied);
    }

    [Fact]
    public void Endpoints_ExposeAptStatutoryOrdinanceAvailabilityReadPolicyMetadata()
    {
        using var factory = CreateFactory();
        _ = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/apt/statutory-discounts/ordinance-availability", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().HaveCount(2);
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .OnlyContain(policy => policy == AptStatutoryOrdinanceAvailabilityEndpoints.ReadPolicy);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(AptStatutoryOrdinanceAvailabilityEndpoints.ReadPolicy)
            .Should()
            .Equal(ReadPermission);
    }

    private static CustomWebApplicationFactory CreateFactory(
        IAptStatutoryOrdinanceAvailabilityService? service = null,
        bool enableRbac = false) =>
        new CustomWebApplicationFactory()
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = enableRbac ? "true" : "false",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "true"
            })
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IAptStatutoryOrdinanceAvailabilityService>();
                services.AddSingleton(service ?? new FakeAvailabilityService(AptStatutoryOrdinanceAvailabilityValues.Available));
                services.RemoveAll<ICentralPmsRbacRepository>();
                services.AddSingleton<ICentralPmsRbacRepository>(new FakeRbacRepository());
            });

    private static AptStatutoryOrdinanceAvailabilityRequest Request(string entitlementType = "SENIOR_CITIZEN") =>
        new(
            SiteGroupId.ToString("D"),
            SiteId.ToString("D"),
            "APT-TERMINAL-001",
            "FAKE-PMS",
            ParkingSessionId.ToString("D"),
            null,
            null,
            entitlementType,
            Guid.NewGuid());

    private static void AddAptHeaders(HttpClient client, Guid siteId)
    {
        client.DefaultRequestHeaders.Add("X-Site-Id", siteId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, ServiceIdentityId.ToString("D"));
        client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, ReadPermission);
    }

    private static readonly string[] ForbiddenWorkflowTables =
    [
        "discounts.statutory_discount_decision_commands",
        "discounts.statutory_discount_decision_policy_authorities",
        "discounts.statutory_discount_payable_basis_application_commands",
        "discounts.statutory_discount_payable_basis_applications",
        "discounts.statutory_discount_validations",
        "operator_console.statutory_discount_service_channel_reviews",
        "core.payment_attempts",
        "core.payment_confirmations",
        "payments.provider_sessions",
        "payments.provider_callbacks",
        "payments.provider_outcomes",
        "payments.provider_status_queries",
        "core.terminal_cash_payment_commands",
        "core.terminal_cash_payment_command_audits",
        "core.parking_sessions",
        "core.tariff_snapshots",
        "discounts.statutory_discount_policy_registry",
        "discounts.statutory_discount_policy_versions",
        "discounts.statutory_discount_policy_registry_lgu_scopes",
        "core.fiscal_issuance_references",
        "core.fiscal_issuance_attempt_history",
        "core.fiscal_issuance_exception_reviews",
        "core.fiscal_issuance_readback_reconciliations",
        "core.fiscal_issuance_retry_command_preparations",
        "core.fiscal_issuance_retry_schedule_preparations",
        "core.fiscal_issuance_retry_execution_attempts",
        "gates.gate_commands"
    ];

    private static async Task<IReadOnlyDictionary<string, long>> CaptureForbiddenWorkflowRowCountsAsync()
    {
        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in ForbiddenWorkflowTables)
        {
            var parts = table.Split('.', 2);
            var relationName = $"{QuoteIdentifier(parts[0])}.{QuoteIdentifier(parts[1])}";
            await using var existenceCommand = new NpgsqlCommand(
                "SELECT to_regclass(@relation_name) IS NOT NULL;",
                connection)
            {
                CommandTimeout = 30
            };
            existenceCommand.Parameters.AddWithValue("relation_name", relationName);
            if (!((bool?)await existenceCommand.ExecuteScalarAsync() ?? false))
            {
                continue;
            }

            var sql = $"SELECT COUNT(*) FROM {QuoteIdentifier(parts[0])}.{QuoteIdentifier(parts[1])};";
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
            counts[table] = (long)(await command.ExecuteScalarAsync() ?? 0L);
        }

        return counts;
    }

    private static string QuoteIdentifier(string identifier) =>
        '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private sealed class FakeAvailabilityService : IAptStatutoryOrdinanceAvailabilityService
    {
        private readonly string _classification;

        public FakeAvailabilityService(string classification)
        {
            _classification = classification;
        }

        public Task<AptStatutoryOrdinanceAvailabilityResult> ResolveAsync(
            AptStatutoryOrdinanceAvailabilityRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result("RESOLVE", request, isRevalidate: false));

        public Task<AptStatutoryOrdinanceAvailabilityResult> RevalidateAsync(
            AptStatutoryOrdinanceAvailabilityRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result("REVALIDATE", request, isRevalidate: true));

        private AptStatutoryOrdinanceAvailabilityResult Result(
            string operation,
            AptStatutoryOrdinanceAvailabilityRequest request,
            bool isRevalidate)
        {
            var available = string.Equals(_classification, AptStatutoryOrdinanceAvailabilityValues.Available, StringComparison.Ordinal);
            var response = new AptStatutoryOrdinanceAvailabilityResponse(
                operation,
                isRevalidate ? available ? AptStatutoryOrdinanceAvailabilityValues.PassedUnchanged : AptStatutoryOrdinanceAvailabilityValues.Failed : null,
                _classification,
                request.EntitlementType,
                available,
                available,
                isRevalidate && available,
                available,
                OrdinaryPaymentPreserved: true,
                ParkingSessionId,
                SiteId,
                SiteGroupId,
                "SITE",
                available ? "ACTIVE_COVERED" : "EXPIRED",
                available ? "ACTIVE" : "EXPIRED",
                DateOnly.Parse("2026-01-01"),
                available ? null : DateOnly.Parse("2026-07-01"),
                "UNIT_TEST",
                "SYNTHETIC_LGU",
                "POLICY-SAFE",
                request.CorrelationId,
                DateTimeOffset.Parse("2026-08-03T02:30:00Z"),
                DateTimeOffset.Parse("2026-08-03T02:00:00Z"),
                Retryable: false,
                available ? "Available." : "Not available.");

            return new AptStatutoryOrdinanceAvailabilityResult(true, 200, request.CorrelationId, response, null, null, false);
        }
    }

    private sealed class FakeRbacRepository : ICentralPmsRbacRepository
    {
        public Task<bool> UserHasAnyPermissionAsync(
            Guid userId,
            IReadOnlyCollection<string> permissionCodes,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> ServiceIdentityIsActiveAsync(
            Guid serviceIdentityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task RecordDeniedAsync(
            string policyName,
            Guid? userId,
            Guid? serviceIdentityId,
            Guid? correlationId,
            string requestPath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

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
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AptStatutoryOrdinanceAvailabilityNoWriteCollection
{
    public const string Name = "APT statutory ordinance availability no-write proof";
}
