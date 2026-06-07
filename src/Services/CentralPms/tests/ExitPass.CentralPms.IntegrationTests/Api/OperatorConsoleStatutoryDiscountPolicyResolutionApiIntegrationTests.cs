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
    private const string PolicyLguCode = "PH-INT-193";
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
        body.FreeDurationMinutes.Should().BeNull();
        body.SucceedingHoursDiscountRule.Should().Be("APPLY_NATIONAL_STATUTORY_DISCOUNT");
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
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
    }

    private static async Task PreparePolicyResolutionFixtureAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM discounts.discount_policy_references
            WHERE policy_code IN (
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'PH_RA10754_PWD_NATIONAL_FALLBACK',
                'INTEGRATION_POLICY_RESOLUTION_LOCAL_SC',
                'INTEGRATION_POLICY_RESOLUTION_UNVERIFIED_SC'
            );

            UPDATE sites.sites
               SET lgu_code = @lgu_code,
                   updated_at = now()
             WHERE site_id = @site_id;

            INSERT INTO discounts.discount_policy_references (
                discount_policy_reference_id,
                policy_code,
                policy_name,
                policy_description,
                policy_type,
                policy_level,
                entitlement_type,
                national_law_reference,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                policy_status,
                effective_from
            )
            VALUES (
                '6e000000-0000-0000-0000-000000000101',
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'RA 9994 Senior Citizen National Fallback',
                'Integration fallback policy.',
                'LEGAL_REFERENCE',
                'NATIONAL_LAW',
                'SENIOR_CITIZEN',
                'RA 9994',
                100,
                'integration-v1',
                true,
                true,
                'ACTIVE',
                now() - interval '1 day'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET policy_status = EXCLUDED.policy_status,
                updated_at = now();

            INSERT INTO discounts.discount_policy_references (
                discount_policy_reference_id,
                policy_code,
                policy_name,
                policy_description,
                policy_type,
                policy_level,
                entitlement_type,
                national_law_reference,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                policy_status,
                effective_from
            )
            VALUES (
                '6e000000-0000-0000-0000-000000000102',
                'PH_RA10754_PWD_NATIONAL_FALLBACK',
                'RA 10754 PWD National Fallback',
                'Integration fallback policy.',
                'LEGAL_REFERENCE',
                'NATIONAL_LAW',
                'PWD',
                'RA 10754',
                100,
                'integration-v1',
                true,
                true,
                'ACTIVE',
                now() - interval '1 day'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET policy_status = EXCLUDED.policy_status,
                updated_at = now();

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = PolicyLguCode;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertVerifiedLocalPolicyAsync()
    {
        const string sql = """
            INSERT INTO discounts.discount_policy_references (
                discount_policy_reference_id,
                policy_code,
                policy_name,
                policy_description,
                policy_type,
                policy_level,
                entitlement_type,
                local_ordinance_reference,
                lgu_code,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                effective_from,
                policy_status
            )
            VALUES (
                @policy_id,
                'INTEGRATION_POLICY_RESOLUTION_LOCAL_SC',
                'Integration Local Senior Policy',
                'Integration test verified local policy.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'SENIOR_CITIZEN',
                'INTEGRATION-ORD-193',
                @lgu_code,
                10,
                'integration-v1',
                true,
                true,
                now() - interval '1 day',
                'ACTIVE'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET lgu_code = EXCLUDED.lgu_code,
                policy_status = EXCLUDED.policy_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = LocalPolicyId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = PolicyLguCode;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertUnverifiedLocalPolicyAsync()
    {
        const string sql = """
            INSERT INTO discounts.discount_policy_references (
                discount_policy_reference_id,
                policy_code,
                policy_name,
                policy_description,
                policy_type,
                policy_level,
                entitlement_type,
                local_ordinance_reference,
                lgu_code,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                effective_from,
                policy_status
            )
            VALUES (
                @policy_id,
                'INTEGRATION_POLICY_RESOLUTION_UNVERIFIED_SC',
                'Integration Unverified Senior Policy',
                'Integration test unverified local policy.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'SENIOR_CITIZEN',
                'INTEGRATION-UNVERIFIED-193',
                @lgu_code,
                10,
                'integration-v1',
                true,
                true,
                now() - interval '1 day',
                'DRAFT'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET lgu_code = EXCLUDED.lgu_code,
                policy_status = EXCLUDED.policy_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = UnverifiedPolicyId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = PolicyLguCode;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearFixtureSiteJurisdictionAsync()
    {
        const string sql = """
            UPDATE sites.sites
               SET lgu_code = NULL,
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
