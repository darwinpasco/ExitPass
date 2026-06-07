using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console statutory discount policy resolution API.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountPolicyResolutionApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/statutory-discounts/resolve-policy";
    private static readonly Guid FixtureUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid InactiveHrUserId = Guid.Parse("77000000-0000-0000-0000-000000000011");
    private static readonly Guid FixtureDeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid FixtureSiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid FixtureSiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid FixtureShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid InactiveHrShiftId = Guid.Parse("77000000-0000-0000-0000-000000000051");
    private static readonly Guid FixtureParkingSessionId = Guid.Parse("77000000-0000-0000-0000-000000000090");
    private static readonly Guid PolicyJurisdictionId = Guid.Parse("6e000000-0000-0000-0000-000000000001");
    private static readonly Guid MissingJurisdictionSiteId = Guid.Parse("6e000000-0000-0000-0000-000000000002");
    private static readonly Guid LocalPolicyId = Guid.Parse("6e000000-0000-0000-0000-000000000003");
    private static readonly Guid UnverifiedPolicyId = Guid.Parse("6e000000-0000-0000-0000-000000000004");

    /// <summary>
    /// Verifies the documented policy resolution route exists.
    /// </summary>
    [Fact]
    public void ResolvePolicyEndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == Endpoint)
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>
    /// Verifies the documented policy resolution route is discoverable through Swagger/OpenAPI.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyEndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/resolve-policy");
        swaggerJson.Should().Contain("ResolveOperatorConsoleStatutoryDiscountPolicy");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies Senior Citizen fallback resolves to RA 9994 and does not grant free parking.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenNoLocalSeniorPolicy_ReturnsRa9994Fallback()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();

        var beforeBoundaryCount = await CountNonReadOnlyBoundaryRowsAsync();
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request("SENIOR_CITIZEN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.AccessPersisted.Should().BeTrue();
        body.PolicyResolved.Should().BeTrue();
        body.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        body.NationalLawReference.Should().Be("RA 9994");
        body.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        body.FreeDurationMinutes.Should().BeNull();
        body.InitialRateExempt.Should().BeFalse();
        body.FullFeeExempt.Should().BeFalse();
        body.PolicySnapshot.Should().NotBeNull();

        var afterBoundaryCount = await CountNonReadOnlyBoundaryRowsAsync();
        afterBoundaryCount.Should().Be(beforeBoundaryCount);
    }

    /// <summary>
    /// Verifies PWD fallback resolves to RA 10754 and does not grant free parking.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenNoLocalPwdPolicy_ReturnsRa10754Fallback()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request("PWD"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.PolicyResolved.Should().BeTrue();
        body.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        body.NationalLawReference.Should().Be("RA 10754");
        body.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        body.FreeDurationMinutes.Should().BeNull();
        body.InitialRateExempt.Should().BeFalse();
        body.FullFeeExempt.Should().BeFalse();
    }

    /// <summary>
    /// Verifies verified local policy resolves before national fallback.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenVerifiedLocalPolicyExists_ReturnsLocalPolicy()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();
        await InsertVerifiedLocalPolicyAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request("SENIOR_CITIZEN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.PolicyResolved.Should().BeTrue();
        body.StatutoryDiscountPolicyId.Should().Be(LocalPolicyId);
        body.PolicyResolutionBasis.Should().Be("LOCAL_ORDINANCE_APPLIED");
        body.OrdinanceReference.Should().Be("INTEGRATION-ORD-193");
        body.NationalLawReference.Should().BeNull();
        body.FreeDurationMinutes.Should().Be(120);
        body.SucceedingHoursDiscountRule.Should().Be("REGULAR_RATE");
    }

    /// <summary>
    /// Verifies unverified local policy does not auto-resolve or fall through silently.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenUnverifiedLocalPolicyExists_ReturnsUnverifiedDeterministicResult()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();
        await InsertUnverifiedLocalPolicyAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request("SENIOR_CITIZEN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.PolicyResolved.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_UNVERIFIED");
    }

    /// <summary>
    /// Verifies access denial prevents policy resolution.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenAccessDenied_DoesNotResolvePolicy()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            Request(
                "SENIOR_CITIZEN",
                userId: InactiveHrUserId,
                operatorShiftId: InactiveHrShiftId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body.AccessPersisted.Should().BeTrue();
        body.PolicyResolved.Should().BeFalse();
        body.PolicyCode.Should().BeNull();
    }

    /// <summary>
    /// Verifies missing jurisdiction mapping fails closed.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenSiteHasNoJurisdiction_ReturnsDeterministicFailure()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();
        await ClearFixtureSiteJurisdictionAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            Request("SENIOR_CITIZEN"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.PolicyResolved.Should().BeFalse();
        body.ErrorCode.Should().Be("SITE_JURISDICTION_NOT_CONFIGURED");
    }

    /// <summary>
    /// Verifies unsupported entitlement types fail deterministically.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenEntitlementUnsupported_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request("OTHER_STATUTORY"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies site group mismatch is stopped by access evaluation before policy resolution.
    /// </summary>
    [Fact]
    public async Task ResolvePolicy_WhenSiteGroupMismatch_AccessDeniedBeforePolicyResolution()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PreparePolicyResolutionFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            Request("SENIOR_CITIZEN", siteGroupId: Guid.Parse("6e000000-0000-0000-0000-000000000099")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body!.PolicyResolved.Should().BeFalse();
        body.PolicyCode.Should().BeNull();
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionRequest Request(
        string entitlementType,
        Guid? userId = null,
        Guid? siteId = null,
        Guid? siteGroupId = null,
        Guid? operatorShiftId = null) =>
        new(
            userId ?? FixtureUserId,
            FixtureDeviceBindingId,
            siteId ?? FixtureSiteId,
            siteGroupId ?? FixtureSiteGroupId,
            operatorShiftId ?? FixtureShiftId,
            FixtureParkingSessionId,
            entitlementType,
            $"operator-console-policy-resolution-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static async Task SeedManualFixtureAsync()
    {
        var sql = ReadRepoFile(
            "infra",
            "db",
            "fixtures",
            "operator-console-access-evaluation",
            "Seed-OperatorConsoleAccessEvaluationManualFixtures.sql");

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PreparePolicyResolutionFixtureAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE policy_code IN (
                'INTEGRATION_POLICY_RESOLUTION_LOCAL_SC',
                'INTEGRATION_POLICY_RESOLUTION_UNVERIFIED_SC'
            );

            INSERT INTO sites.jurisdictions (
                jurisdiction_id,
                country_code,
                province_name,
                city_municipality_name,
                psgc_code,
                lgu_code,
                jurisdiction_type,
                jurisdiction_status,
                source_reference
            )
            VALUES (
                @jurisdiction_id,
                'PH',
                'Integration Province',
                'Integration City',
                '999999999',
                'PH-INT-193',
                'CITY_MUNICIPALITY',
                'ACTIVE',
                'Integration policy resolution jurisdiction'
            )
            ON CONFLICT (jurisdiction_id) DO UPDATE
            SET jurisdiction_status = EXCLUDED.jurisdiction_status,
                updated_at = now();

            UPDATE sites.sites
               SET jurisdiction_id = @jurisdiction_id,
                   updated_at = now()
             WHERE site_id = @site_id;

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = PolicyJurisdictionId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertVerifiedLocalPolicyAsync()
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_id,
                jurisdiction_id,
                policy_code,
                policy_name,
                entitlement_type,
                policy_resolution_basis,
                policy_level,
                policy_type,
                ordinance_reference,
                verification_status,
                beneficiary_residency_scope,
                benefit_type,
                free_duration_minutes,
                initial_rate_exempt_flag,
                full_fee_exempt_flag,
                free_period_application,
                succeeding_hours_discount_rule,
                discount_base_scope,
                stacking_policy,
                legal_basis_priority,
                requires_operator_validation,
                requires_evidence,
                effective_from,
                policy_status,
                source_reference,
                reviewed_at,
                policy_snapshot_json
            )
            VALUES (
                @policy_id,
                @jurisdiction_id,
                'INTEGRATION_POLICY_RESOLUTION_LOCAL_SC',
                'Integration Local Senior Policy',
                'SENIOR_CITIZEN',
                'LOCAL_ORDINANCE_APPLIED',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'INTEGRATION-ORD-193',
                'VERIFIED_OFFICIAL',
                'NON_RESIDENT_ALLOWED',
                'FREE_DURATION',
                120,
                false,
                false,
                'BEFORE_DISCOUNT_COMPUTATION',
                'REGULAR_RATE',
                'CHARGEABLE_PORTION_ONLY',
                'NO_STACKING_ON_FREE_PERIOD',
                'LOCAL_ORDINANCE_FIRST',
                true,
                true,
                DATE '2026-01-01',
                'ACTIVE',
                'Integration test verified local policy.',
                now(),
                '{}'::jsonb
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET jurisdiction_id = EXCLUDED.jurisdiction_id,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = LocalPolicyId;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = PolicyJurisdictionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertUnverifiedLocalPolicyAsync()
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_id,
                jurisdiction_id,
                policy_code,
                policy_name,
                entitlement_type,
                policy_resolution_basis,
                policy_level,
                policy_type,
                ordinance_reference,
                verification_status,
                beneficiary_residency_scope,
                benefit_type,
                free_duration_minutes,
                initial_rate_exempt_flag,
                full_fee_exempt_flag,
                free_period_application,
                succeeding_hours_discount_rule,
                discount_base_scope,
                stacking_policy,
                legal_basis_priority,
                requires_operator_validation,
                requires_evidence,
                effective_from,
                policy_status,
                source_reference,
                policy_snapshot_json
            )
            VALUES (
                @policy_id,
                @jurisdiction_id,
                'INTEGRATION_POLICY_RESOLUTION_UNVERIFIED_SC',
                'Integration Unverified Senior Policy',
                'SENIOR_CITIZEN',
                'LOCAL_ORDINANCE_APPLIED',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'INTEGRATION-UNVERIFIED-193',
                'LEAD_UNVERIFIED',
                'UNVERIFIED',
                'FREE_DURATION',
                60,
                false,
                false,
                'BEFORE_DISCOUNT_COMPUTATION',
                'REGULAR_RATE',
                'CHARGEABLE_PORTION_ONLY',
                'NO_STACKING_ON_FREE_PERIOD',
                'LOCAL_ORDINANCE_FIRST',
                true,
                true,
                DATE '2026-01-01',
                'DRAFT',
                'Integration test unverified local policy.',
                '{}'::jsonb
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET jurisdiction_id = EXCLUDED.jurisdiction_id,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = UnverifiedPolicyId;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = PolicyJurisdictionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearFixtureSiteJurisdictionAsync()
    {
        const string sql = """
            UPDATE sites.sites
               SET jurisdiction_id = NULL,
                   updated_at = now()
             WHERE site_id = @site_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountNonReadOnlyBoundaryRowsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM discounts.statutory_discount_validations)
              + (SELECT COUNT(*) FROM core.tariff_snapshots WHERE statutory_discount_validation_id IS NOT NULL AND statutory_discount_amount > 0)
              + (SELECT COUNT(*) FROM core.payment_attempts)
              + (SELECT COUNT(*) FROM core.payment_confirmations)
              + (SELECT COUNT(*) FROM core.exit_authorizations)
              + (SELECT COUNT(*) FROM gates.gate_authorization_consumptions)
              + (SELECT COUNT(*) FROM coupons.coupon_applications)
              + (SELECT COUNT(*) FROM payments.provider_outcomes)
              + (SELECT COUNT(*) FROM reconciliation.reconciliation_items) AS boundary_count;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<bool> CanOpenDatabaseAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }
}
