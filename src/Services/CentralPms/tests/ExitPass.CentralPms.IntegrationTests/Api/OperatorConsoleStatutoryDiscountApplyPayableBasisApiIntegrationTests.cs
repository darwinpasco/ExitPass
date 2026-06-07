using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
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
    private static readonly Guid MissingPolicyContextValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000195");
    private static readonly Guid InvalidPolicySnapshotValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000196");
    private static readonly Guid UnsupportedFreeDurationValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000197");
    private static readonly Guid InvalidPolicyIdSnapshotValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000198");
    private static readonly Guid MismatchedPolicyIdSnapshotValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000199");
    private static readonly Guid PaymentAttemptGuardrailValidationId = Guid.Parse("4c000000-0000-0000-0000-00000000019a");

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
        swaggerJson.Should().Contain("may create an applied tariff snapshot plus statutory discount payable-basis application evidence");
        swaggerJson.Should().NotContain("does not create final APPLIED tariff snapshots");
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
        applied.ApplicationStatus.Should().Be("APPLIED");
        applied.AlreadyApplied.Should().BeFalse();
        applied.StatutoryDiscountValidationId.Should().Be(draft.DraftId.Value);
        applied.OriginalTariffSnapshotId.Should().Be(FixtureOriginalTariffSnapshotId);
        applied.AppliedTariffSnapshotId.Should().NotBeNull();
        applied.GrossAmountMinorUnits.Should().Be(12500);
        applied.VatExclusiveAmountMinorUnits.Should().Be(11161);
        applied.VatAmountMinorUnits.Should().Be(1339);
        applied.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        applied.FinalPayableAmountMinorUnits.Should().Be(8929);
        applied.CurrencyCode.Should().Be("PHP");
        applied.StatutoryDiscountPolicyId.Should().Be(NoEvidencePolicyId);
        applied.ResolvedJurisdictionId.Should().Be(FixtureJurisdictionId);
        applied.PolicyResolutionBasis.Should().Be("LOCAL_ORDINANCE_APPLIED");
        applied.PolicyCode.Should().Be("INTEGRATION_OPERATOR_CONSOLE_NO_EVIDENCE_POLICY");
        applied.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        applied.OrdinanceReference.Should().Be("INTEGRATION-NO-EVIDENCE-195");
        applied.PolicySnapshotUsed.Should().BeTrue();

        var rowCount = await CountApplicationsAsync(draft.DraftId.Value);
        rowCount.Should().Be(1);
        var computationBasis = await ReadApplicationComputationBasisAsync(draft.DraftId.Value);
        computationBasis.GetProperty("policyContext").GetProperty("policyCode").GetString()
            .Should().Be("INTEGRATION_OPERATOR_CONSOLE_NO_EVIDENCE_POLICY");
        computationBasis.GetProperty("policyContext").GetProperty("benefitType").GetString()
            .Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        computationBasis.GetProperty("policyContext").GetProperty("ordinanceReference").GetString()
            .Should().Be("INTEGRATION-NO-EVIDENCE-195");
        var originalSnapshot = await ReadTariffSnapshotAsync(FixtureOriginalTariffSnapshotId);
        originalSnapshot.Status.Should().Be("SUPERSEDED");
        originalSnapshot.GrossAmount.Should().Be(125.00m);
        originalSnapshot.StatutoryDiscountAmount.Should().Be(0.00m);
        originalSnapshot.NetAmount.Should().Be(125.00m);
        var appliedSnapshot = await ReadTariffSnapshotAsync(applied.AppliedTariffSnapshotId!.Value);
        appliedSnapshot.Status.Should().Be("ACTIVE");
        appliedSnapshot.GrossAmount.Should().Be(125.00m);
        appliedSnapshot.StatutoryDiscountAmount.Should().Be(22.32m);
        appliedSnapshot.NetAmount.Should().Be(89.29m);
        (await CountAppliedTariffSnapshotsAsync(draft.DraftId.Value)).Should().Be(1);

        using var replayResponse = await client.PostAsJsonAsync(
            ApplyEndpoint(draft.DraftId.Value),
            ApplyRequest());

        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await replayResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        replay.Should().NotBeNull();
        replay!.ApplicationAccepted.Should().BeTrue();
        replay.ApplicationPersisted.Should().BeTrue();
        replay.AlreadyApplied.Should().BeTrue();
        replay.PayableBasisApplicationId.Should().Be(applied.PayableBasisApplicationId);
        replay.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        replay.ApplicationStatus.Should().Be("APPLIED");
        replay.PolicySnapshotUsed.Should().BeTrue();
        replay.PolicyCode.Should().Be(applied.PolicyCode);
        (await CountApplicationsAsync(draft.DraftId.Value)).Should().Be(1);
        (await CountAppliedTariffSnapshotsAsync(draft.DraftId.Value)).Should().Be(1);

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

    /// <summary>
    /// Verifies approved validations without persisted policy context fail deterministically.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenPolicyContextMissing_ReturnsDeterministicError()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();
        await InsertApprovedValidationAsync(MissingPolicyContextValidationId, policyId: null, snapshotJson: null);

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(MissingPolicyContextValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_CONTEXT_MISSING");
        body.PolicySnapshotUsed.Should().BeFalse();
        (await CountApplicationsAsync(MissingPolicyContextValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies invalid persisted policy snapshot content fails deterministically.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenPolicySnapshotInvalid_ReturnsDeterministicError()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();
        await InsertApprovedValidationAsync(InvalidPolicySnapshotValidationId, NoEvidencePolicyId, "{}");

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(InvalidPolicySnapshotValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID");
        (await CountApplicationsAsync(InvalidPolicySnapshotValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies a non-Guid statutoryDiscountPolicyId inside the persisted snapshot fails deterministically.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenPolicySnapshotPolicyIdInvalid_ReturnsDeterministicError()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();
        await InsertApprovedValidationAsync(
            InvalidPolicyIdSnapshotValidationId,
            NoEvidencePolicyId,
            VatExemptPolicySnapshot("not-a-guid"));

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(InvalidPolicyIdSnapshotValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID");
        (await CountApplicationsAsync(InvalidPolicyIdSnapshotValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies a snapshot policy ID that differs from the validation policy ID fails before application insert.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenPolicySnapshotPolicyIdMismatchesValidation_ReturnsDeterministicError()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();
        await InsertApprovedValidationAsync(
            MismatchedPolicyIdSnapshotValidationId,
            NoEvidencePolicyId,
            VatExemptPolicySnapshot("6f000000-0000-0000-0000-000000000102"));

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(MismatchedPolicyIdSnapshotValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_SNAPSHOT_INVALID");
        (await CountApplicationsAsync(MismatchedPolicyIdSnapshotValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies local free-duration policies are not processed through generic VAT-exempt computation.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenPolicyBenefitFreeDuration_ReturnsUnsupportedBenefit()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();
        await InsertApprovedValidationAsync(
            UnsupportedFreeDurationValidationId,
            Guid.Parse("77000000-0000-0000-0000-000000000261"),
            FreeDurationPolicySnapshot());

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(UnsupportedFreeDurationValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ErrorCode.Should().Be("POLICY_BENEFIT_TYPE_NOT_SUPPORTED_FOR_PAYABLE_APPLICATION");
        body.PolicyResolutionBasis.Should().Be("LOCAL_ORDINANCE_APPLIED");
        (await CountApplicationsAsync(UnsupportedFreeDurationValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies existing payment attempts block applied snapshot creation and keep the original tariff active.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenPaymentAttemptExists_DoesNotCreateAppliedSnapshot()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync();
        await InsertApprovedValidationAsync(
            PaymentAttemptGuardrailValidationId,
            NoEvidencePolicyId,
            VatExemptPolicySnapshot(NoEvidencePolicyId.ToString()));
        await InsertPaymentAttemptAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(PaymentAttemptGuardrailValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("PAYMENT_ATTEMPT_ALREADY_EXISTS");
        (await CountApplicationsAsync(PaymentAttemptGuardrailValidationId)).Should().Be(0);
        (await CountAppliedTariffSnapshotsAsync(PaymentAttemptGuardrailValidationId)).Should().Be(0);

        var originalSnapshot = await ReadTariffSnapshotAsync(FixtureOriginalTariffSnapshotId);
        originalSnapshot.Status.Should().Be("ACTIVE");
        originalSnapshot.GrossAmount.Should().Be(125.00m);
        originalSnapshot.StatutoryDiscountAmount.Should().Be(0.00m);
        originalSnapshot.NetAmount.Should().Be(125.00m);
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

            DELETE FROM core.payment_attempts
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

            DELETE FROM core.payment_attempts
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

    private static async Task InsertPaymentAttemptAsync()
    {
        const string sql = """
            INSERT INTO core.payment_attempts (
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                idempotency_key,
                payment_rail_id,
                currency_code,
                amount,
                attempt_status,
                requested_at,
                expires_at,
                finalized_at,
                failure_reason_code,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @payment_attempt_id,
                @parking_session_id,
                @tariff_snapshot_id,
                @idempotency_key,
                NULL,
                'PHP',
                125.00,
                'REQUESTED'::core.payment_attempt_status_enum,
                now(),
                now() + interval '15 minutes',
                NULL,
                NULL,
                @correlation_id,
                now(),
                @created_by_service_identity_id,
                now(),
                @updated_by_service_identity_id,
                1
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = FixtureOriginalTariffSnapshotId;
        command.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = $"integration-apply-payment-guardrail-{Guid.NewGuid():N}";
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("created_by_service_identity_id", NpgsqlDbType.Uuid).Value = FixtureServiceIdentityId;
        command.Parameters.Add("updated_by_service_identity_id", NpgsqlDbType.Uuid).Value = FixtureServiceIdentityId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountApplicationsAsync(Guid validationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM core.tariff_snapshots
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND statutory_discount_amount > 0;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAppliedTariffSnapshotsAsync(Guid validationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM core.tariff_snapshots
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<JsonElement> ReadApplicationComputationBasisAsync(Guid validationId)
    {
        const string sql = """
            SELECT jsonb_build_object(
                'policyContext',
                jsonb_build_object('benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT')
            )::text
            FROM core.tariff_snapshots
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND statutory_discount_amount > 0;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;

        var json = (string?)await command.ExecuteScalarAsync();
        json.Should().NotBeNull();
        return JsonDocument.Parse(json!).RootElement.Clone();
    }

    private static async Task InsertApprovedValidationAsync(Guid validationId, Guid? policyId, string? snapshotJson)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_validations (
                statutory_discount_validation_id,
                parking_session_id,
                tariff_snapshot_id,
                entitlement_type,
                policy_resolution_basis,
                local_ordinance_applied,
                national_law_fallback_applied,
                validation_channel,
                validation_status,
                currency_code,
                evidence_required,
                evidence_captured,
                requested_at,
                validated_at,
                validated_by_user_id,
                requested_by_user_id,
                correlation_id,
                created_by_user_id,
                updated_by_user_id,
                statutory_discount_policy_id,
                resolved_jurisdiction_id,
                resolved_policy_snapshot_json
            )
            VALUES (
                @validation_id,
                @parking_session_id,
                @tariff_snapshot_id,
                'SENIOR_CITIZEN',
                'LOCAL_ORDINANCE_APPLIED',
                true,
                false,
                'OPERATOR_ASSISTED',
                'APPROVED',
                'PHP',
                false,
                false,
                now(),
                now(),
                @user_id,
                @user_id,
                @correlation_id,
                @user_id,
                @user_id,
                @policy_id,
                @jurisdiction_id,
                @snapshot_json::jsonb
            );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = FixtureOriginalTariffSnapshotId;
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = FixtureUserId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = Guid.NewGuid();
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId.HasValue ? policyId.Value : DBNull.Value;
        command.Parameters.Add("jurisdiction_id", NpgsqlDbType.Uuid).Value = policyId.HasValue ? FixtureJurisdictionId : DBNull.Value;
        command.Parameters.Add("snapshot_json", NpgsqlDbType.Jsonb).Value = snapshotJson is null ? DBNull.Value : snapshotJson;
        await command.ExecuteNonQueryAsync();
    }

    private static string FreeDurationPolicySnapshot() =>
        JsonSerializer.Serialize(new
        {
            statutoryDiscountPolicyId = "77000000-0000-0000-0000-000000000261",
            policyCode = "MANUAL_TEST_QC_VERIFIED_LOCAL_POLICY",
            policyName = "Manual Test Verified Local Senior Citizen Policy",
            entitlementType = "SENIOR_CITIZEN",
            policyResolutionBasis = "LOCAL_ORDINANCE_APPLIED",
            policyLevel = "LOCAL_ORDINANCE",
            policyType = "LOCAL_ORDINANCE",
            legalBasisReference = "Manual verified local ordinance fixture for policy resolution smoke testing.",
            ordinanceReference = "MANUAL-QC-ORD-193",
            nationalLawReference = (string?)null,
            verificationStatus = "VERIFIED_OFFICIAL",
            benefitType = "FREE_DURATION",
            freeDurationMinutes = 120,
            initialRateExempt = false,
            fullFeeExempt = false,
            freePeriodApplication = "BEFORE_DISCOUNT_COMPUTATION",
            succeedingHoursDiscountRule = "REGULAR_RATE",
            discountBaseScope = "CHARGEABLE_PORTION_ONLY",
            stackingPolicy = "NO_STACKING_ON_FREE_PERIOD",
            legalBasisPriority = "LOCAL_ORDINANCE_FIRST",
            requiresEvidence = false
        });

    private static string VatExemptPolicySnapshot(string statutoryDiscountPolicyId) =>
        JsonSerializer.Serialize(new
        {
            statutoryDiscountPolicyId,
            policyCode = "INTEGRATION_OPERATOR_CONSOLE_NO_EVIDENCE_POLICY",
            policyName = "Integration Operator Console No Evidence Policy",
            entitlementType = "SENIOR_CITIZEN",
            policyResolutionBasis = "LOCAL_ORDINANCE_APPLIED",
            policyLevel = "LOCAL_ORDINANCE",
            policyType = "LOCAL_ORDINANCE",
            legalBasisReference = (string?)null,
            ordinanceReference = "INTEGRATION-NO-EVIDENCE-195",
            nationalLawReference = (string?)null,
            verificationStatus = "VERIFIED_OFFICIAL",
            benefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT",
            freeDurationMinutes = (int?)null,
            initialRateExempt = false,
            fullFeeExempt = false,
            freePeriodApplication = "NOT_APPLICABLE",
            succeedingHoursDiscountRule = "REGULAR_RATE",
            discountBaseScope = "CHARGEABLE_PORTION_ONLY",
            stackingPolicy = "NO_STACKING_ON_FREE_PERIOD",
            legalBasisPriority = "LOCAL_ORDINANCE_FIRST",
            requiresEvidence = false
        });

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
