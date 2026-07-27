using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console statutory discount decision API route and state transition behavior.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountDecisionApiIntegrationTests
{
    private const string DraftEndpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private const string DecisionEndpointTemplate = "/v1/ops/operator-console/statutory-discounts/{0}/decision";
    private static readonly Guid EvaluationId = Guid.Parse("4a000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("4a000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("4a000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("4a000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("4a000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("4a000000-0000-0000-0000-000000000006");
    private static readonly Guid DraftId = Guid.Parse("4a000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("4a000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("4a000000-0000-0000-0000-000000000009");
    private static readonly Guid FixtureUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid FixtureDeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid FixtureSiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid FixtureSiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid FixtureShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid FixtureReviewerUserId = Guid.Parse("77000000-0000-0000-0000-000000000012");
    private static readonly Guid FixtureParkingSessionId = Guid.Parse("77000000-0000-0000-0000-000000000090");
    private static readonly Guid FixtureJurisdictionId = Guid.Parse("77000000-0000-0000-0000-000000000211");
    private const string FixtureLguCode = "PH-INT-NO-EVIDENCE-195";
    private static readonly Guid NoEvidencePolicyId = Guid.Parse("6f000000-0000-0000-0000-000000000101");

    /// <summary>
    /// Verifies the documented Operator Console statutory discount decision route exists.
    /// </summary>
    [Fact]
    public void DecisionEndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/statutory-discounts/{draftId:guid}/decision")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>
    /// Verifies the documented decision route is discoverable through Swagger/OpenAPI.
    /// </summary>
    [Fact]
    public async Task DecisionEndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/{draftId}/decision");
        swaggerJson.Should().Contain("DecideOperatorConsoleStatutoryDiscount");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies denied access returns a deterministic 200 response without decision persistence.
    /// </summary>
    [Fact]
    public async Task Decision_WhenAccessDenied_ReturnsDeniedEnvelope()
    {
        using var factory = CreateFactory(DeniedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(DecisionEndpoint(DraftId), DecisionRequest("APPROVE"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeFalse();
        body.AccessPersisted.Should().BeTrue();
        body.DecisionAccepted.Should().BeFalse();
        body.DecisionPersisted.Should().BeFalse();
        body.DraftId.Should().Be(DraftId);
    }

    /// <summary>
    /// Verifies validation errors map to Central PMS error envelopes.
    /// </summary>
    [Fact]
    public async Task Decision_WhenRequestInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(AcceptedResult(), throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(DecisionEndpoint(DraftId), DecisionRequest("APPROVE"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    /// <summary>
    /// Verifies missing drafts map to 404 without a decision transition.
    /// </summary>
    [Fact]
    public async Task Decision_WhenDraftMissing_ReturnsNotFoundEnvelope()
    {
        using var factory = CreateFactory(NotFoundResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(DecisionEndpoint(DraftId), DecisionRequest("APPROVE"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DecisionAccepted.Should().BeFalse();
        body.ErrorCode.Should().Be("DRAFT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies live decision state transitions, replay behavior, evidence gating, and non-payment boundaries.
    /// </summary>
    [Fact]
    public async Task Decision_LiveFixture_TransitionsDeterministicallyWithoutPaymentBoundaryWrites()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureDecisionDraftsAsync();

        using var factory = CreateAllowedAccessFactory();
        using var client = factory.CreateClient();
        var beforeBoundaryCount = await CountPaymentBoundaryRecordsAsync(FixtureParkingSessionId);

        var draft = await CreateDraftAsync(client, entitlementType: "SENIOR_CITIZEN", evidenceCaptureRequested: false);
        using var sameRequesterApproveResponse = await client.PostAsJsonAsync(
            DecisionEndpoint(draft.DraftId!.Value),
            DecisionRequest("APPROVE", useFixtureAccessContext: true));
        sameRequesterApproveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sameRequesterApprove = await sameRequesterApproveResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        sameRequesterApprove.Should().NotBeNull();
        sameRequesterApprove!.AccessAllowed.Should().BeTrue();
        sameRequesterApprove.DecisionAccepted.Should().BeFalse();
        sameRequesterApprove.DecisionPersisted.Should().BeFalse();
        sameRequesterApprove.CurrentValidationStatus.Should().Be("REQUESTED");
        sameRequesterApprove.ErrorCode.Should().Be("REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT");
        (await ReadDraftStatusAsync(draft.DraftId.Value)).Should().Be("REQUESTED");
        (await CountApplicationsForValidationAsync(draft.DraftId.Value)).Should().Be(0);

        using var sameRequesterRejectResponse = await client.PostAsJsonAsync(
            DecisionEndpoint(draft.DraftId.Value),
            DecisionRequest("REJECT", "REQUESTER_SELF_REJECT", useFixtureAccessContext: true));
        sameRequesterRejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sameRequesterReject = await sameRequesterRejectResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        sameRequesterReject.Should().NotBeNull();
        sameRequesterReject!.DecisionAccepted.Should().BeFalse();
        sameRequesterReject.DecisionPersisted.Should().BeFalse();
        sameRequesterReject.CurrentValidationStatus.Should().Be("REQUESTED");
        sameRequesterReject.ErrorCode.Should().Be("REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT");
        (await ReadDraftStatusAsync(draft.DraftId.Value)).Should().Be("REQUESTED");

        var approve = DecisionRequest("APPROVE", useFixtureAccessContext: true, useFixtureReviewer: true);
        using var approveResponse = await client.PostAsJsonAsync(DecisionEndpoint(draft.DraftId!.Value), approve);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        approved.Should().NotBeNull();
        approved!.AccessAllowed.Should().BeTrue();
        approved.DecisionAccepted.Should().BeTrue();
        approved.DecisionPersisted.Should().BeTrue();
        approved.PreviousValidationStatus.Should().Be("REQUESTED");
        approved.CurrentValidationStatus.Should().Be("APPROVED");
        approved.DecisionChanged.Should().BeTrue();
        approved.StatutoryDiscountDecisionCommandId.Should().NotBeNull();

        var approvedRow = await ReadDraftStatusAsync(draft.DraftId.Value);
        approvedRow.Should().Be("APPROVED");
        var approvedReviewer = await ReadValidatedByUserIdAsync(draft.DraftId.Value);
        approvedReviewer.Should().Be(FixtureReviewerUserId);
        var canonicalDecisionId = await ReadCanonicalDecisionCommandIdForValidationAsync(draft.DraftId.Value);
        canonicalDecisionId.Should().Be(approved.StatutoryDiscountDecisionCommandId);
        (await CountApplicationCommandsForDecisionAsync(canonicalDecisionId!.Value)).Should().Be(0);

        using var replayResponse = await client.PostAsJsonAsync(DecisionEndpoint(draft.DraftId.Value), approve);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await replayResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        replay.Should().NotBeNull();
        replay!.AlreadyDecided.Should().BeTrue();
        replay.DecisionChanged.Should().BeFalse();
        replay.StatutoryDiscountDecisionCommandId.Should().Be(canonicalDecisionId);

        using var conflictResponse = await client.PostAsJsonAsync(DecisionEndpoint(draft.DraftId.Value), DecisionRequest("REJECT", "OPPOSITE_DECISION", useFixtureAccessContext: true, useFixtureReviewer: true));
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        conflict.Should().NotBeNull();
        conflict!.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DRAFT_ALREADY_DECIDED");

        await ResetFixtureDecisionDraftsAsync();
        var evidenceDraft = await CreateDraftAsync(client, entitlementType: "SENIOR_CITIZEN", evidenceCaptureRequested: true);
        using var blockedApproveResponse = await client.PostAsJsonAsync(DecisionEndpoint(evidenceDraft.DraftId!.Value), DecisionRequest("APPROVE", useFixtureAccessContext: true, useFixtureReviewer: true));
        blockedApproveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockedApprove = await blockedApproveResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        blockedApprove.Should().NotBeNull();
        blockedApprove!.DecisionAccepted.Should().BeFalse();
        blockedApprove.ErrorCode.Should().Be("EVIDENCE_REQUIRED_NOT_CAPTURED");
        (await ReadDraftStatusAsync(evidenceDraft.DraftId.Value)).Should().Be("REQUESTED");

        using var rejectResponse = await client.PostAsJsonAsync(DecisionEndpoint(evidenceDraft.DraftId.Value), DecisionRequest("REJECT", "ID_NOT_VALID", useFixtureAccessContext: true, useFixtureReviewer: true));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>();
        rejected.Should().NotBeNull();
        rejected!.DecisionAccepted.Should().BeTrue();
        rejected.CurrentValidationStatus.Should().Be("REJECTED");
        rejected.DecisionReasonCode.Should().Be("ID_NOT_VALID");
        rejected.StatutoryDiscountDecisionCommandId.Should().NotBeNull();
        (await CountApplicationCommandsForDecisionAsync(rejected.StatutoryDiscountDecisionCommandId!.Value)).Should().Be(0);

        var afterBoundaryCount = await CountPaymentBoundaryRecordsAsync(FixtureParkingSessionId);
        afterBoundaryCount.Should().Be(beforeBoundaryCount);
    }

    private static CustomWebApplicationFactory CreateFactory(
        OperatorConsoleStatutoryDiscountDecisionResult result,
        bool throwValidation = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountDecisionService>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountDecisionService>(
                    new FakeStatutoryDiscountDecisionService(result, throwValidation));
            });

    private static CustomWebApplicationFactory CreateAllowedAccessFactory() =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAllowedAccessEvaluationService());
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAllowedAccessEvaluationWriter());
            });

    private static async Task<OperatorConsoleStatutoryDiscountDraftResponse> CreateDraftAsync(
        HttpClient client,
        string entitlementType,
        bool evidenceCaptureRequested)
    {
        using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest(entitlementType, evidenceCaptureRequested));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.DraftAccepted.Should().BeTrue();
        body.DraftPersisted.Should().BeTrue();
        return body;
    }

    private static OperatorConsoleStatutoryDiscountDraftRequest DraftRequest(
        string entitlementType,
        bool evidenceCaptureRequested) =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            FixtureParkingSessionId,
            "MANUAL-SESSION-LOOKUP-001",
            PlateNumber: null,
            entitlementType,
            entitlementType == "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID",
            entitlementType == "PWD" ? "NCDA" : "OSCA",
            ExpiryDate: null,
            entitlementType == "PWD" ? "PWD4" : "1234",
            EntitlementFingerprint: null,
            evidenceCaptureRequested,
            evidenceCaptureRequested ? "SUPERVISOR_REVIEW" : null,
            OperatorAttestation: true,
            AttestationNotes: "Integration decision test draft only.",
            ReasonCode: "INTEGRATION_DECISION_TEST",
            $"operator-console-statutory-discount-decision-draft-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDecisionRequest DecisionRequest(
        string decision,
        string? reason = null,
        bool useFixtureAccessContext = false,
        bool useFixtureReviewer = false) =>
        new(
            useFixtureAccessContext ? useFixtureReviewer ? FixtureReviewerUserId : FixtureUserId : UserId,
            useFixtureAccessContext ? FixtureDeviceBindingId : DeviceBindingId,
            useFixtureAccessContext ? FixtureSiteId : SiteId,
            useFixtureAccessContext ? FixtureSiteGroupId : SiteGroupId,
            useFixtureAccessContext ? FixtureShiftId : ShiftId,
            decision,
            reason,
            DecisionNotes: "Integration decision test.",
            ReviewerAttestation: true,
            $"operator-console-statutory-discount-decision-{Guid.NewGuid():N}",
            CorrelationId);

    private static string DecisionEndpoint(Guid draftId) =>
        string.Format(DecisionEndpointTemplate, draftId);

    private static OperatorConsoleStatutoryDiscountDecisionResult DeniedResult() =>
        new(
            EvaluationId,
            AccessAllowed: false,
            "DENIED",
            ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            DecisionAccepted: false,
            DecisionPersisted: false,
            DraftId,
            ParkingSessionId: null,
            EntitlementType: null,
            PreviousValidationStatus: null,
            CurrentValidationStatus: null,
            Decision: "APPROVE",
            DecisionReasonCode: null,
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionResult AcceptedResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DecisionAccepted: true,
            DecisionPersisted: true,
            DraftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            "APPROVED",
            "APPROVE",
            DecisionReasonCode: null,
            AlreadyDecided: false,
            DecisionChanged: true,
            IneligibilityReason: null,
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionResult NotFoundResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DecisionAccepted: false,
            DecisionPersisted: false,
            DraftId,
            ParkingSessionId: null,
            EntitlementType: null,
            PreviousValidationStatus: null,
            CurrentValidationStatus: null,
            Decision: "APPROVE",
            DecisionReasonCode: null,
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: "DRAFT_NOT_FOUND",
            ErrorCode: "DRAFT_NOT_FOUND",
            CorrelationId);

    private sealed class FakeStatutoryDiscountDecisionService : IOperatorConsoleStatutoryDiscountDecisionService
    {
        private readonly OperatorConsoleStatutoryDiscountDecisionResult _result;
        private readonly bool _throwValidation;

        public FakeStatutoryDiscountDecisionService(
            OperatorConsoleStatutoryDiscountDecisionResult result,
            bool throwValidation)
        {
            _result = result;
            _throwValidation = throwValidation;
        }

        public Task<OperatorConsoleStatutoryDiscountDecisionResult> DecideAsync(
            OperatorConsoleStatutoryDiscountDecisionCommand command,
            CancellationToken cancellationToken)
        {
            if (_throwValidation)
            {
                throw new ArgumentException("Decision is required.");
            }

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeAllowedAccessEvaluationService : IOperatorConsoleAccessEvaluationService
    {
        public Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
            OperatorConsoleAccessEvaluationCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(AllowedAccessResult(command) with { EvaluationId = Guid.Empty, Persisted = false });
    }

    private sealed class FakeAllowedAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
    {
        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with { EvaluationId = Guid.NewGuid(), Persisted = true });
    }

    private static OperatorConsoleAccessEvaluationResult AllowedAccessResult(
        OperatorConsoleAccessEvaluationCommand command) =>
        new(
            Guid.Empty,
            Allowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            "OPERATOR",
            new OperatorConsoleDeviceTrustResult(command.OperatorDeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(command.OperatorShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(command.SiteId, command.SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-07-12T10:00:00+08:00"),
            Persisted: false,
            command.CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                command.UserId,
                HrIdentityMappingId: null,
                command.OperatorDeviceBindingId,
                command.OperatorShiftId,
                ShiftTakeoverId: null,
                command.SiteGroupId,
                command.SiteId,
                command.ControlledActionCode,
                command.WorkflowCode,
                command.ParkingSessionId.HasValue ? "PARKING_SESSION" : null,
                command.ParkingSessionId));

    private static async Task SeedManualFixtureAsync()
    {
        await ApplyCanonicalDecisionConvergencePatchesAsync();
        await ClearPayableBasisApplyStateAsync();
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
        await InsertNoEvidenceLocalPolicyAsync();
    }

    private static async Task ApplyCanonicalDecisionConvergencePatchesAsync()
    {
        await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
    }

    private static async Task InsertNoEvidenceLocalPolicyAsync()
    {
        const string sql = """
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
                'INTEGRATION_OPERATOR_CONSOLE_NO_EVIDENCE_POLICY',
                'Integration Operator Console No Evidence Policy',
                'Integration test local policy to keep existing decision/apply approval paths evidence-optional.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'SENIOR_CITIZEN',
                'INTEGRATION-NO-EVIDENCE-195',
                @lgu_code,
                10,
                'integration-v1',
                true,
                false,
                now() - interval '1 day',
                'ACTIVE'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET lgu_code = EXCLUDED.lgu_code,
                requires_evidence_capture = EXCLUDED.requires_evidence_capture,
                policy_status = EXCLUDED.policy_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = NoEvidencePolicyId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = FixtureLguCode;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = FixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearPayableBasisApplyStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD');

            UPDATE discounts.statutory_discount_validations
               SET tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
               SET superseded_by_tariff_snapshot_id = NULL,
                   statutory_discount_validation_id = NULL
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

    private static async Task ResetFixtureDecisionDraftsAsync()
    {
        const string sql = """
            DELETE FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD');

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
                  AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
                  AND validation_channel = 'OPERATOR_ASSISTED'
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id
              AND statutory_discount_validation_id IN (
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
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = FixtureParkingSessionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadDraftStatusAsync(Guid draftId)
    {
        const string sql = """
            SELECT validation_status::text
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task<Guid?> ReadValidatedByUserIdAsync(Guid draftId)
    {
        const string sql = """
            SELECT validated_by_user_id
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? null : (Guid)value;
    }

    private static async Task<Guid?> ReadCanonicalDecisionCommandIdForValidationAsync(Guid validationId)
    {
        const string sql = """
            SELECT statutory_discount_decision_command_id
            FROM discounts.statutory_discount_decision_commands
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND semantic_hash_source_version = 'statutory-discount-decision:sha256:v2'
            ORDER BY completed_at DESC NULLS LAST, created_at DESC
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? null : (Guid)value;
    }

    private static async Task<int> CountApplicationCommandsForDecisionAsync(Guid statutoryDiscountDecisionCommandId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountApplicationsForValidationAsync(Guid validationId)
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
        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count);
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
