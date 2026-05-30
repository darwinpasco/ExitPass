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
/// Verifies the Operator Console statutory discount payable-basis application API.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountApplyPayableBasisApiIntegrationTests
{
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string DecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/decision";
    private const string ApplyEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/apply-payable-basis";
    private static readonly Guid FixtureUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid FixtureDeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid FixtureSiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid FixtureSiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid FixtureShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid FixtureParkingSessionId = Guid.Parse("4c000000-0000-0000-0000-000000000090");
    private static readonly Guid FixtureOriginalTariffSnapshotId = Guid.Parse("4c000000-0000-0000-0000-000000000091");
    private static readonly Guid FixtureVendorSystemId = Guid.Parse("77000000-0000-0000-0000-000000000004");
    private static readonly Guid FixtureServiceIdentityId = Guid.Parse("77000000-0000-0000-0000-000000000003");
    private static readonly Guid FixtureJurisdictionId = Guid.Parse("77000000-0000-0000-0000-000000000211");
    private static readonly Guid NoEvidencePolicyId = Guid.Parse("6f000000-0000-0000-0000-000000000101");

    /// <summary>
    /// Verifies the documented Operator Console apply-payable-basis route exists.
    /// </summary>
    [Fact]
    public void ApplyPayableBasisEndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/statutory-discounts/{validationId:guid}/apply-payable-basis")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>
    /// Verifies the documented apply-payable-basis route is discoverable through Swagger/OpenAPI.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasisEndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis");
        swaggerJson.Should().Contain("ApplyOperatorConsoleStatutoryDiscountPayableBasis");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies a live approved statutory discount validation can be applied once and replayed deterministically.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_LiveFixture_AppliesAndReplaysWithoutPaymentBoundaryWrites()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var beforeBoundaryCount = await CountPaymentBoundaryRecordsAsync(FixtureParkingSessionId);

        var draft = await CreateDraftAsync(client);
        var decision = await ApproveDraftAsync(client, draft.DraftId!.Value);
        decision.CurrentValidationStatus.Should().Be("APPROVED");

        using var applyResponse = await client.PostAsJsonAsync(
            ApplyEndpoint(draft.DraftId.Value),
            ApplyRequest());

        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = await applyResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        applied.Should().NotBeNull();
        applied!.AccessAllowed.Should().BeTrue();
        applied.ApplicationAccepted.Should().BeTrue();
        applied.ApplicationPersisted.Should().BeTrue();
        applied.ApplicationStatus.Should().Be("REQUESTED");
        applied.AlreadyApplied.Should().BeFalse();
        applied.StatutoryDiscountValidationId.Should().Be(draft.DraftId.Value);
        applied.OriginalTariffSnapshotId.Should().Be(FixtureOriginalTariffSnapshotId);
        applied.AppliedTariffSnapshotId.Should().BeNull();
        applied.GrossAmountMinorUnits.Should().Be(12500);
        applied.VatExclusiveAmountMinorUnits.Should().Be(11161);
        applied.VatAmountMinorUnits.Should().Be(1339);
        applied.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        applied.FinalPayableAmountMinorUnits.Should().Be(8929);
        applied.CurrencyCode.Should().Be("PHP");

        var rowCount = await CountApplicationsAsync(draft.DraftId.Value);
        rowCount.Should().Be(1);
        var originalSnapshot = await ReadTariffSnapshotAsync(FixtureOriginalTariffSnapshotId);
        originalSnapshot.Status.Should().Be("ACTIVE");
        originalSnapshot.GrossAmount.Should().Be(125.00m);
        originalSnapshot.StatutoryDiscountAmount.Should().Be(0.00m);
        originalSnapshot.NetAmount.Should().Be(125.00m);

        using var replayResponse = await client.PostAsJsonAsync(
            ApplyEndpoint(draft.DraftId.Value),
            ApplyRequest());

        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await replayResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        replay.Should().NotBeNull();
        replay!.ApplicationAccepted.Should().BeTrue();
        replay.ApplicationPersisted.Should().BeTrue();
        replay.AlreadyApplied.Should().BeFalse();
        replay.PayableBasisApplicationId.Should().Be(applied.PayableBasisApplicationId);
        replay.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        (await CountApplicationsAsync(draft.DraftId.Value)).Should().Be(1);

        var afterBoundaryCount = await CountPaymentBoundaryRecordsAsync(FixtureParkingSessionId);
        afterBoundaryCount.Should().Be(beforeBoundaryCount);
    }

    /// <summary>
    /// Verifies not-approved validations are rejected deterministically.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenValidationNotApproved_ReturnsDeterministicError()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var draft = await CreateDraftAsync(client);

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(draft.DraftId!.Value),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_NOT_APPROVED");
        (await CountApplicationsAsync(draft.DraftId.Value)).Should().Be(0);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftResponse> CreateDraftAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.DraftAccepted.Should().BeTrue();
        body.DraftPersisted.Should().BeTrue();
        return body;
    }

    private static async Task<OperatorConsoleStatutoryDiscountDecisionResponse> ApproveDraftAsync(HttpClient client, Guid draftId)
    {
        using var response = await client.PostAsJsonAsync(DecisionEndpoint(draftId), DecisionRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        body.Should().NotBeNull();
        body!.DecisionAccepted.Should().BeTrue();
        return body;
    }

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest() =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureParkingSessionId,
            "INTEGRATION-APPLY-SESSION-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "1234",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: false,
            EvidenceAccessIntent: null,
            OperatorAttestation: true,
            AttestationNotes: "Integration payable-basis apply test draft only.",
            ReasonCode: "INTEGRATION_APPLY_TEST",
            $"operator-console-statutory-discount-apply-draft-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDecisionRequest DecisionRequest() =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            "APPROVE",
            DecisionReasonCode: null,
            DecisionNotes: "Integration payable-basis apply approval.",
            ReviewerAttestation: true,
            $"operator-console-statutory-discount-apply-decision-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisRequest ApplyRequest() =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureOriginalTariffSnapshotId,
            $"operator-console-statutory-discount-apply-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static string DecisionEndpoint(Guid draftId) => string.Format(DecisionEndpointTemplate, draftId);

    private static string ApplyEndpoint(Guid validationId) => string.Format(ApplyEndpointTemplate, validationId);

    private static async Task SeedManualFixtureAsync()
    {
        await ClearPayableBasisApplyStateAsync();

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
        await InsertNoEvidenceLocalPolicyAsync();
    }

    private static async Task InsertNoEvidenceLocalPolicyAsync()
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
                'INTEGRATION_OPERATOR_CONSOLE_NO_EVIDENCE_POLICY',
                'Integration Operator Console No Evidence Policy',
                'SENIOR_CITIZEN',
                'LOCAL_ORDINANCE_APPLIED',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'INTEGRATION-NO-EVIDENCE-195',
                'VERIFIED_OFFICIAL',
                'NON_RESIDENT_ALLOWED',
                'STATUTORY_DISCOUNT_VAT_EXEMPT',
                NULL,
                false,
                false,
                'NOT_APPLICABLE',
                'REGULAR_RATE',
                'CHARGEABLE_PORTION_ONLY',
                'NO_STACKING_ON_FREE_PERIOD',
                'LOCAL_ORDINANCE_FIRST',
                true,
                false,
                DATE '2026-01-01',
                'ACTIVE',
                'Integration test local policy to keep existing decision/apply approval paths evidence-optional.',
                now(),
                '{}'::jsonb
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET jurisdiction_id = EXCLUDED.jurisdiction_id,
                requires_evidence = EXCLUDED.requires_evidence,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = NoEvidencePolicyId;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = FixtureJurisdictionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearPayableBasisApplyStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            UPDATE discounts.statutory_discount_validations
               SET tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
               SET superseded_by_tariff_snapshot_id = NULL,
                   statutory_discount_validation_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id;

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertParkingSessionAsync()
    {
        const string sql = """
            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                vendor_session_ref,
                plate_number_hash,
                plate_number_masked,
                ticket_number_hash,
                ticket_number_masked,
                entry_at,
                vendor_session_status,
                session_status,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_id,
                'INTEGRATION-APPLY-SESSION-001',
                '130c6e1f29c1a9714e55d22de13d48f88f5adbe70d9a27b34068c8e6a07b9011',
                'APL-188',
                'bff73440a421cae8515fb71f8a1f76db48f4f05d01133c853d7bdaf7752eadc2',
                'INTEGRATION-APPLY-SESSION-001',
                '2026-05-29T00:00:00Z',
                'ACTIVE',
                'ACTIVE',
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = FixtureSiteGroupId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = FixtureVendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = FixtureServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = FixtureServiceIdentityId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ResetFixtureApplyStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
                  AND validation_channel = 'OPERATOR_ASSISTED'
            );

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
              AND validation_channel = 'OPERATOR_ASSISTED';

            UPDATE core.tariff_snapshots
               SET superseded_by_tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id;

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertBaseTariffSnapshotAsync()
    {
        const string sql = """
            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                snapshot_status,
                calculated_at,
                expires_at,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            VALUES (
                @tariff_snapshot_id,
                @parking_session_id,
                @vendor_system_id,
                'INTEGRATION-OPERATOR-CONSOLE-APPLY',
                'INTEGRATION-V1',
                'PHP',
                125.00,
                0,
                0,
                125.00,
                'ACTIVE'::core.tariff_snapshot_status_enum,
                now(),
                now() + interval '1 hour',
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = FixtureOriginalTariffSnapshotId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = FixtureVendorSystemId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = FixtureServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = FixtureServiceIdentityId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountApplicationsAsync(Guid validationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(string Status, decimal GrossAmount, decimal StatutoryDiscountAmount, decimal NetAmount)> ReadTariffSnapshotAsync(Guid tariffSnapshotId)
    {
        const string sql = """
            SELECT snapshot_status::text, gross_amount, statutory_discount_amount, net_amount
            FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected tariff snapshot fixture row was not found.");
        }

        return (reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3));
    }

    private static async Task<int> CountPaymentBoundaryRecordsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM core.payment_confirmations pc
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM gates.gate_authorization_consumptions gac
                   JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
                  WHERE ea.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*) FROM coupons.coupon_applications WHERE parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM payments.provider_outcomes po
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id)
              + (SELECT COUNT(*)
                   FROM reconciliation.reconciliation_items ri
                   JOIN core.payment_attempts pa ON pa.payment_attempt_id = ri.payment_attempt_id
                  WHERE pa.parking_session_id = @parking_session_id) AS boundary_count;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
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
