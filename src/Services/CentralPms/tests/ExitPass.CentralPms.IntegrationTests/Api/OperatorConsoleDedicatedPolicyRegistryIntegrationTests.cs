using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Infrastructure.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies Operator Console statutory discount behavior against the dedicated policy registry.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleDedicatedPolicyRegistryIntegrationTests
{
    private const string ResolveEndpoint = "/v1/ops/operator-console/statutory-discounts/resolve-policy";
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string FixtureLguCode = "PH-INT-DR-258";
    private static readonly Guid FixtureUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid FixtureDeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid FixtureSiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid FixtureSiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid FixtureShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid FixtureParkingSessionId = Guid.Parse("77000000-0000-0000-0000-000000000090");
    private static readonly Guid SeniorReadyPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000001");
    private static readonly Guid PwdReadyPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000002");
    private static readonly Guid PilotPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000003");
    private static readonly Guid LeadPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000004");
    private static readonly Guid SandboxPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000005");
    private static readonly Guid CompatibilityPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000006");
    private static readonly Guid DraftReadyPolicyId = Guid.Parse("25800000-0000-0000-0000-000000000007");

    /// <summary>
    /// Verifies repository resolution prefers the dedicated registry even when a compatibility row also matches.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenDedicatedAndCompatibilityRowsMatch_ReturnsDedicatedRegistryRow()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SeniorReadyPolicyId,
                "DUMMY_OC_POLICY_SC_READY_VERIFIED",
                "Dummy OC Senior Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertCompatibilityLocalPolicyAsync(
                CompatibilityPolicyId,
                "DUMMY_OC_POLICY_COMPATIBILITY_SHOULD_NOT_WIN",
                "SENIOR_CITIZEN");

            var repository = new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());

            var result = await repository.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
                    FixtureSiteId,
                    FixtureSiteGroupId,
                    "SENIOR_CITIZEN",
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                CancellationToken.None);

            result.Resolved.Should().BeTrue();
            result.Policy.Should().NotBeNull();
            result.Policy!.StatutoryDiscountPolicyId.Should().Be(SeniorReadyPolicyId);
            result.Policy.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_READY_VERIFIED");
            result.Policy.VerificationStatus.Should().Be("ACTIVE_APPROVED");
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies the policy resolution API returns dedicated Senior Citizen and PWD ready policies.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedReadyPoliciesExist_ReturnsReadyVerifiedForSeniorAndPwd()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SeniorReadyPolicyId,
                "DUMMY_OC_POLICY_SC_READY_VERIFIED",
                "Dummy OC Senior Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertDedicatedPolicyAsync(
                PwdReadyPolicyId,
                "DUMMY_OC_POLICY_PWD_READY_VERIFIED",
                "Dummy OC PWD Ready Verified Policy",
                "PWD",
                "ACTIVE_APPROVED",
                "PWD_ID");

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var seniorResponse = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));
            using var pwdResponse = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("PWD"));

            seniorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            pwdResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var senior = await seniorResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            var pwd = await pwdResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();

            senior.Should().NotBeNull();
            senior!.PolicyResolved.Should().BeTrue();
            senior.StatutoryDiscountPolicyId.Should().Be(SeniorReadyPolicyId);
            senior.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_READY_VERIFIED");
            senior.VerificationStatus.Should().Be("ACTIVE_APPROVED");
            senior.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
            senior.RequiresManualReview.Should().BeFalse();

            pwd.Should().NotBeNull();
            pwd!.PolicyResolved.Should().BeTrue();
            pwd.StatutoryDiscountPolicyId.Should().Be(PwdReadyPolicyId);
            pwd.PolicyCode.Should().Be("DUMMY_OC_POLICY_PWD_READY_VERIFIED");
            pwd.VerificationStatus.Should().Be("ACTIVE_APPROVED");
            pwd.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
            pwd.RequiresManualReview.Should().BeFalse();
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry pilot rows remain manual-review classified.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedPilotPolicyExists_ReturnsReadyWithManualReview()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                PilotPolicyId,
                "DUMMY_OC_POLICY_SC_APPROVED_FOR_PILOT",
                "Dummy OC Senior Pilot Policy",
                "SENIOR_CITIZEN",
                "APPROVED_FOR_PILOT",
                "SENIOR_CITIZEN_ID");

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            body.Should().NotBeNull();
            body!.PolicyResolved.Should().BeTrue();
            body.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_APPROVED_FOR_PILOT");
            body.VerificationStatus.Should().Be("APPROVED_FOR_PILOT");
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry unverified/proposed rows do not classify as production-ready.
    /// </summary>
    [Theory]
    [InlineData("LEAD_UNVERIFIED", "DUMMY_OC_POLICY_SC_LEAD_UNVERIFIED")]
    [InlineData("PROPOSED_ONLY", "DUMMY_OC_POLICY_SC_PROPOSED_ONLY")]
    public async Task ResolvePolicyApi_WhenDedicatedPolicyIsNotVerified_ReturnsConfiguredButUnverified(
        string verificationStatus,
        string policyCode)
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            var policyStatus = verificationStatus == "PROPOSED_ONLY" ? "DRAFT" : "ACTIVE";
            await InsertDedicatedPolicyAsync(
                LeadPolicyId,
                policyCode,
                $"Dummy OC Senior {verificationStatus} Policy",
                "SENIOR_CITIZEN",
                verificationStatus,
                "SENIOR_CITIZEN_ID",
                policyStatus);

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            body.Should().NotBeNull();
            body!.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified);
            if (verificationStatus == "PROPOSED_ONLY")
            {
                body.PolicyResolved.Should().BeFalse();
                body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_UNVERIFIED");
            }
            else
            {
                body.PolicyResolved.Should().BeTrue();
                body.PolicyCode.Should().Be(policyCode);
            }

            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry sandbox/test markers remain sandbox-only.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedPolicyHasTestMarker_ReturnsSandboxOnly()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SandboxPolicyId,
                "TEST_OC_POLICY_SC_SANDBOX_ONLY",
                "Test OC Senior Sandbox Only Policy",
                "SENIOR_CITIZEN",
                "VERIFIED_OFFICIAL",
                "SENIOR_CITIZEN_ID");

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>();
            body.Should().NotBeNull();
            body!.PolicyResolved.Should().BeTrue();
            body.PolicyCode.Should().Be("TEST_OC_POLICY_SC_SANDBOX_ONLY");
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies production draft creation can use a ready dedicated policy during the hybrid FK transition.
    /// </summary>
    [Fact]
    public async Task DraftApi_WhenProductionAndDedicatedReadyPolicyExists_CreatesDraft()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                DraftReadyPolicyId,
                "DUMMY_OC_POLICY_SC_DRAFT_READY_VERIFIED",
                "Dummy OC Senior Draft Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            await InsertCompatibilityLocalPolicyAsync(
                DraftReadyPolicyId,
                "DUMMY_OC_POLICY_SC_DRAFT_READY_VERIFIED",
                "SENIOR_CITIZEN");

            using var factory = CreateProductionFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
            body.Should().NotBeNull();
            body!.DraftAccepted.Should().BeTrue();
            body.DraftPersisted.Should().BeTrue();
            body.StatutoryDiscountPolicyId.Should().Be(DraftReadyPolicyId);
            body.PolicyCode.Should().Be("DUMMY_OC_POLICY_SC_DRAFT_READY_VERIFIED");
            body.VerificationStatus.Should().Be("ACTIVE_APPROVED");
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
            body.RequiresManualReview.Should().BeFalse();
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies production draft creation fails closed for dedicated registry sandbox/test policies.
    /// </summary>
    [Fact]
    public async Task DraftApi_WhenProductionAndDedicatedPolicyHasTestMarker_DoesNotCreateDraft()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SandboxPolicyId,
                "TEST_OC_POLICY_SC_SANDBOX_ONLY",
                "Test OC Senior Sandbox Only Policy",
                "SENIOR_CITIZEN",
                "VERIFIED_OFFICIAL",
                "SENIOR_CITIZEN_ID");

            using var factory = CreateProductionFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
            body.Should().NotBeNull();
            body!.DraftAccepted.Should().BeFalse();
            body.DraftPersisted.Should().BeFalse();
            body.DraftId.Should().BeNull();
            body.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
            body.RequiresManualReview.Should().BeTrue();
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies dedicated registry resolution does not mutate payment, gate, coupon, or reconciliation boundaries.
    /// </summary>
    [Fact]
    public async Task ResolvePolicyApi_WhenDedicatedPolicyResolved_DoesNotMutateBoundaryTables()
    {
        if (!await CanOpenDatabaseAsync() || !await HasDedicatedRegistryAsync())
        {
            return;
        }

        try
        {
            await PrepareBaseFixtureAsync();
            await InsertDedicatedPolicyAsync(
                SeniorReadyPolicyId,
                "DUMMY_OC_POLICY_SC_READY_VERIFIED",
                "Dummy OC Senior Ready Verified Policy",
                "SENIOR_CITIZEN",
                "ACTIVE_APPROVED",
                "SENIOR_CITIZEN_ID");
            var before = await CountBoundaryRowsAsync();

            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(ResolveEndpoint, ResolveRequest("SENIOR_CITIZEN"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var after = await CountBoundaryRowsAsync();
            after.Should().Be(before);
        }
        finally
        {
            await CleanupDedicatedRegistryFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies the production policy readiness SQL remains read-only.
    /// </summary>
    [Fact]
    public void ProductionPolicyReadinessSql_ContainsNoMutationKeywords()
    {
        var sql = ReadRepoFile("scripts", "operator-console", "Verify-ProductionPolicyRegistryReadiness.sql");
        var withoutBlockComments = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var withoutLineComments = Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

        withoutLineComments.Should().NotMatchRegex(
            @"\b(INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|TRUNCATE|MERGE|GRANT|REVOKE|CALL|DO|EXECUTE)\b");
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionRequest ResolveRequest(string entitlementType) =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureParkingSessionId,
            entitlementType,
            $"operator-console-dedicated-registry-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest() =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureParkingSessionId,
            "DEDICATED-REGISTRY-TEST-TICKET",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            "****2580",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: false,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "Dedicated registry integration test attestation.",
            ReasonCode: "DEDICATED_REGISTRY_TEST",
            $"operator-console-dedicated-draft-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static CustomWebApplicationFactory CreateProductionFactory() =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<OperatorConsolePolicyReadinessEnvironment>();
                services.AddSingleton(new OperatorConsolePolicyReadinessEnvironment("Production"));
            });

    private static async Task PrepareBaseFixtureAsync()
    {
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
        await CleanupDedicatedRegistryFixtureAsync();

        const string sql = """
            UPDATE sites.sites
               SET lgu_code = @lgu_code,
                   updated_at = now()
             WHERE site_id = @site_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertDedicatedPolicyAsync(
        Guid policyId,
        string policyCode,
        string policyName,
        string entitlementType,
        string verificationStatus,
        string requiredEvidenceType,
        string policyStatus = "ACTIVE")
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_policy_registry (
                statutory_discount_policy_registry_id,
                policy_code,
                policy_name,
                policy_description,
                entitlement_type,
                policy_status,
                verification_status,
                policy_level,
                policy_type,
                policy_resolution_basis,
                benefit_type,
                discount_base_scope,
                jurisdiction_code,
                jurisdiction_name,
                site_group_id,
                site_id,
                beneficiary_residency_scope,
                facility_scope,
                requires_evidence,
                required_evidence_type,
                requires_operator_validation,
                legal_basis_reference,
                ordinance_reference,
                source_reference,
                reviewed_by,
                reviewed_at,
                approved_by,
                approved_at,
                effective_from,
                effective_to,
                notes,
                correlation_id
            )
            VALUES (
                @policy_id,
                @policy_code,
                @policy_name,
                'Dedicated registry integration fixture row.',
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @policy_status::discounts.discount_policy_status_enum,
                @verification_status::discounts.policy_verification_status_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_level_enum,
                'LOCAL_ORDINANCE'::discounts.discount_policy_type_enum,
                'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                @jurisdiction_code,
                'Dummy Integration Jurisdiction',
                @site_group_id,
                @site_id,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                'Dedicated registry integration fixture only.',
                true,
                @required_evidence_type::discounts.discount_evidence_type_enum,
                true,
                'DUMMY-LEGAL-258',
                'DUMMY-ORD-258',
                'DUMMY-SOURCE-258',
                'test-reviewer-258',
                now() - interval '2 days',
                CASE
                    WHEN @verification_status IN ('APPROVED_FOR_PILOT', 'ACTIVE_APPROVED') THEN 'test-approver-258'
                    ELSE NULL
                END,
                CASE
                    WHEN @verification_status IN ('APPROVED_FOR_PILOT', 'ACTIVE_APPROVED') THEN now() - interval '1 day'
                    ELSE NULL
                END,
                now() - interval '1 day',
                NULL,
                'Non-production dedicated registry integration fixture.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                required_evidence_type = EXCLUDED.required_evidence_type,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                approved_by = EXCLUDED.approved_by,
                approved_at = EXCLUDED.approved_at,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId;
        command.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = policyCode;
        command.Parameters.Add("policy_name", NpgsqlDbType.Varchar).Value = policyName;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("policy_status", NpgsqlDbType.Text).Value = policyStatus;
        command.Parameters.Add("verification_status", NpgsqlDbType.Text).Value = verificationStatus;
        command.Parameters.Add("required_evidence_type", NpgsqlDbType.Text).Value = requiredEvidenceType;
        command.Parameters.Add("jurisdiction_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = FixtureSiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCompatibilityLocalPolicyAsync(Guid policyId, string policyCode, string entitlementType)
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
                site_group_id,
                site_id,
                precedence_rank,
                policy_version,
                requires_operator_validation,
                requires_evidence_capture,
                effective_from,
                policy_status
            )
            VALUES (
                @policy_id,
                @policy_code,
                @policy_name,
                'Compatibility mirror for dedicated registry integration test.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                'TEST-ORD-258',
                @lgu_code,
                @site_group_id,
                @site_id,
                10,
                'dedicated-registry-test-v1',
                true,
                true,
                now() - interval '1 day',
                'ACTIVE'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET discount_policy_reference_id = EXCLUDED.discount_policy_reference_id,
                lgu_code = EXCLUDED.lgu_code,
                site_group_id = EXCLUDED.site_group_id,
                site_id = EXCLUDED.site_id,
                policy_status = EXCLUDED.policy_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId;
        command.Parameters.Add("policy_code", NpgsqlDbType.Varchar).Value = policyCode;
        command.Parameters.Add("policy_name", NpgsqlDbType.Varchar).Value = $"{policyCode} Compatibility Mirror";
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = FixtureSiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupDedicatedRegistryFixtureAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            UPDATE core.tariff_snapshots
               SET statutory_discount_validation_id = NULL,
                   superseded_by_tariff_snapshot_id = NULL
             WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND requested_by_user_id = @user_id
             );

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND requested_by_user_id = @user_id
            );

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id
              AND requested_by_user_id = @user_id;

            DELETE FROM discounts.discount_policy_references
            WHERE policy_code LIKE 'DUMMY_OC_POLICY_%'
               OR policy_code LIKE 'TEST_OC_POLICY_%'
               OR policy_code LIKE 'E2E_OC_POLICY_%'
               OR policy_code LIKE 'SANDBOX_OC_POLICY_%';

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE policy_code LIKE 'DUMMY_OC_POLICY_%'
               OR policy_code LIKE 'TEST_OC_POLICY_%'
               OR policy_code LIKE 'E2E_OC_POLICY_%'
               OR policy_code LIKE 'SANDBOX_OC_POLICY_%';

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = FixtureUserId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasDedicatedRegistryAsync()
    {
        const string sql = """
            SELECT to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> CountBoundaryRowsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts)
              + (SELECT COUNT(*) FROM core.payment_confirmations)
              + (SELECT COUNT(*) FROM core.exit_authorizations)
              + (SELECT COUNT(*) FROM coupons.coupon_applications)
              + (SELECT COUNT(*) FROM gates.gate_authorization_consumptions)
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

    private static string ReadRepoFile(
        string pathPart1,
        string pathPart2,
        string pathPart3,
        [CallerFilePath] string sourceFilePath = "")
    {
        var pathParts = new[] { pathPart1, pathPart2, pathPart3 };
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            var fromSource = TryReadRepoFile(new DirectoryInfo(sourceDirectory), pathParts);
            if (fromSource is not null)
            {
                return fromSource;
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        var fromOutput = TryReadRepoFile(current, pathParts);
        if (fromOutput is not null)
        {
            return fromOutput;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        var fromWorkingDirectory = TryReadRepoFile(current, pathParts);
        if (fromWorkingDirectory is not null)
        {
            return fromWorkingDirectory;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }

    private static string? TryReadRepoFile(DirectoryInfo? current, string[] pathParts)
    {
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

        return null;
    }
}
