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
using System.Text.Json;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console statutory discount draft API route and response mapping.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class OperatorConsoleStatutoryDiscountDraftApiIntegrationTests
{
    private const string Endpoint = "/v1/ops/operator-console/statutory-discounts/draft";
    private static readonly Guid EvaluationId = Guid.Parse("48000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("48000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("48000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("48000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("48000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("48000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("48000000-0000-0000-0000-000000000007");
    private static readonly Guid DraftId = Guid.Parse("48000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("48000000-0000-0000-0000-000000000009");
    private static readonly Guid EvidenceReferenceId = Guid.Parse("48000000-0000-0000-0000-000000000011");
    private static readonly Guid ManualFixtureSiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid ManualFixtureParkingSessionId = Guid.Parse("77000000-0000-0000-0000-000000000090");
    private static readonly Guid DraftPolicyJurisdictionId = Guid.Parse("6f000000-0000-0000-0000-000000000001");
    private const string DraftPolicyLguCode = "PH-INT-DRAFT-195";
    private static readonly Guid DraftVerifiedLocalPolicyId = Guid.Parse("6f000000-0000-0000-0000-000000000002");
    private static readonly Guid DraftUnverifiedLocalPolicyId = Guid.Parse("6f000000-0000-0000-0000-000000000003");

    /// <summary>
    /// Verifies the documented Operator Console statutory discount draft route exists.
    /// </summary>
    [Fact]
    public void EndpointRouteExists()
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
    /// Verifies the documented route is discoverable through Swagger/OpenAPI.
    /// </summary>
    [Fact]
    public async Task EndpointAppearsInSwagger()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        swaggerJson.Should().Contain("/v1/ops/operator-console/statutory-discounts/draft");
        swaggerJson.Should().Contain("DraftOperatorConsoleStatutoryDiscount");
        swaggerJson.Should().Contain("OperatorConsole");
    }

    /// <summary>
    /// Verifies denied access returns a deterministic 200 response without draft details.
    /// </summary>
    [Fact]
    public async Task Draft_WhenAccessDenied_ReturnsDeniedEnvelopeWithoutDraft()
    {
        using var factory = CreateFactory(DeniedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessEvaluationId.Should().Be(EvaluationId);
        body.AccessAllowed.Should().BeFalse();
        body.AccessDecision.Should().Be("DENIED");
        body.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        body.AccessPersisted.Should().BeTrue();
        body.DraftAccepted.Should().BeFalse();
        body.DraftPersisted.Should().BeFalse();
        body.DraftId.Should().BeNull();
    }

    /// <summary>
    /// Verifies accepted drafts return persisted draft evidence.
    /// </summary>
    [Fact]
    public async Task Draft_WhenAccepted_ReturnsDraftEnvelope()
    {
        using var factory = CreateFactory(AcceptedResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DraftAccepted.Should().BeTrue();
        body.DraftPersisted.Should().BeTrue();
        body.DraftId.Should().Be(DraftId);
        body.ValidationStatus.Should().Be("REQUESTED");
        body.EntitlementType.Should().Be("SENIOR_CITIZEN");
        body.EvidenceRequired.Should().BeTrue();
        body.EvidenceReferenceCreated.Should().BeTrue();
        body.EvidenceReferenceId.Should().Be(EvidenceReferenceId);
        body.ReusedExistingDraft.Should().BeFalse();
        body.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        body.NationalLawReference.Should().Be("RA 9994");
    }

    /// <summary>
    /// Verifies replaying the same valid draft request reuses the active draft instead of returning a generic failure.
    /// </summary>
    [Fact]
    public async Task Draft_WhenEquivalentActiveDraftAlreadyExists_ReusesExistingDraft()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var request = ManualFixtureRequest();

        using var firstResponse = await client.PostAsJsonAsync(Endpoint, request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        first.Should().NotBeNull();
        first!.DraftAccepted.Should().BeTrue();
        first.DraftPersisted.Should().BeTrue();
        first.ReusedExistingDraft.Should().BeFalse();

        using var secondResponse = await client.PostAsJsonAsync(Endpoint, request);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        second.Should().NotBeNull();
        second!.DraftAccepted.Should().BeTrue();
        second.DraftPersisted.Should().BeTrue();
        second.ReusedExistingDraft.Should().BeTrue();
        second.DraftId.Should().Be(first.DraftId);
        second.ValidationStatus.Should().Be("REQUESTED");

        var activeDraftCount = await CountActiveDraftsAsync(request.ParkingSessionId, request.EntitlementType);
        activeDraftCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies evidence-requested drafts persist one metadata-only evidence reference and replay reuses it.
    /// </summary>
    [Fact]
    public async Task Draft_WhenEvidenceRequested_PersistsMetadataOnlyEvidenceReferenceAndReplayDoesNotDuplicate()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var request = ManualFixtureRequest(evidenceCaptureRequested: true);

        using var firstResponse = await client.PostAsJsonAsync(Endpoint, request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        first.Should().NotBeNull();
        first!.DraftAccepted.Should().BeTrue();
        first.DraftPersisted.Should().BeTrue();
        first.EvidenceRequired.Should().BeTrue();
        first.EvidenceReferenceCreated.Should().BeTrue();
        first.EvidenceReferenceId.Should().NotBeNull();

        var firstEvidence = await ReadEvidenceReferenceAsync(first.DraftId!.Value, "SENIOR_CITIZEN_ID");
        firstEvidence.Should().NotBeNull();
        firstEvidence!.EvidenceReferenceId.Should().Be(first.EvidenceReferenceId!.Value);
        firstEvidence.EvidenceStorageType.Should().Be("EXTERNAL_REFERENCE");
        firstEvidence.EvidenceStorageRef.Should().BeNull();
        firstEvidence.EvidenceHash.Should().BeNull();
        firstEvidence.EvidenceCaptureStatus.Should().Be("REFERENCED");
        firstEvidence.AccessClassification.Should().Be("RESTRICTED");
        firstEvidence.RedactionStatus.Should().Be("NOT_REDACTED");
        firstEvidence.EvidenceCaptured.Should().BeFalse();

        using var secondResponse = await client.PostAsJsonAsync(Endpoint, request);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        second.Should().NotBeNull();
        second!.DraftAccepted.Should().BeTrue();
        second.ReusedExistingDraft.Should().BeTrue();
        second.DraftId.Should().Be(first.DraftId);
        second.EvidenceRequired.Should().BeTrue();
        second.EvidenceReferenceCreated.Should().BeFalse();
        second.EvidenceReferenceId.Should().Be(first.EvidenceReferenceId);

        var evidenceReferenceCount = await CountEvidenceReferencesAsync(first.DraftId!.Value, "SENIOR_CITIZEN_ID");
        evidenceReferenceCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies Senior Citizen draft creation persists the RA 9994 national fallback policy context.
    /// </summary>
    [Fact]
    public async Task Draft_WhenSeniorFallbackResolved_PersistsRa9994PolicyContext()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PrepareDraftPolicyFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, ManualFixtureRequest(evidenceCaptureRequested: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.DraftAccepted.Should().BeTrue();
        body.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        body.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        body.NationalLawReference.Should().Be("RA 9994");
        body.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        body.FreeDurationMinutes.Should().BeNull();

        var stored = await ReadDraftPolicyContextAsync(body.DraftId!.Value);
        stored.Should().NotBeNull();
        stored!.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        stored.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        stored.Snapshot.GetProperty("nationalLawReference").GetString().Should().Be("RA 9994");
        stored.Snapshot.GetProperty("benefitType").GetString().Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        stored.Snapshot.GetProperty("freeDurationMinutes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Verifies PWD draft creation persists the RA 10754 national fallback policy context.
    /// </summary>
    [Fact]
    public async Task Draft_WhenPwdFallbackResolved_PersistsRa10754PolicyContext()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PrepareDraftPolicyFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, ManualFixtureRequest(entitlementType: "PWD", evidenceCaptureRequested: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.DraftAccepted.Should().BeTrue();
        body.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        body.PolicyCode.Should().Be("PH_RA10754_PWD_NATIONAL_FALLBACK");
        body.NationalLawReference.Should().Be("RA 10754");
        body.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        body.FreeDurationMinutes.Should().BeNull();

        var stored = await ReadDraftPolicyContextAsync(body.DraftId!.Value);
        stored.Should().NotBeNull();
        stored!.Snapshot.GetProperty("nationalLawReference").GetString().Should().Be("RA 10754");
    }

    /// <summary>
    /// Verifies verified local policies are persisted on drafts before national fallback.
    /// </summary>
    [Fact]
    public async Task Draft_WhenVerifiedLocalPolicyExists_PersistsLocalPolicyContext()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PrepareDraftPolicyFixtureAsync();
        await InsertDraftVerifiedLocalPolicyAsync();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(Endpoint, ManualFixtureRequest(evidenceCaptureRequested: false));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
            body.Should().NotBeNull();
            body!.DraftAccepted.Should().BeTrue();
            body.StatutoryDiscountPolicyId.Should().Be(DraftVerifiedLocalPolicyId);
            body.PolicyResolutionBasis.Should().Be("LOCAL_ORDINANCE_APPLIED");
            body.PolicyCode.Should().Be("INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY");
            body.OrdinanceReference.Should().Be("INTEGRATION-DRAFT-ORD-195");
            body.NationalLawReference.Should().BeNull();
            body.FreeDurationMinutes.Should().BeNull();

            var stored = await ReadDraftPolicyContextAsync(body.DraftId!.Value);
            stored.Should().NotBeNull();
            stored!.PolicyId.Should().Be(DraftVerifiedLocalPolicyId);
            stored.PolicyResolutionBasis.Should().Be("LOCAL_ORDINANCE_APPLIED");
            stored.Snapshot.GetProperty("ordinanceReference").GetString().Should().Be("INTEGRATION-DRAFT-ORD-195");
        }
        finally
        {
            await CleanupDraftLocalPolicyFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies unverified local policy blocks draft creation and writes no validation row.
    /// </summary>
    [Fact]
    public async Task Draft_WhenUnverifiedLocalPolicyExists_DoesNotCreateDraft()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PrepareDraftPolicyFixtureAsync();
        await InsertDraftUnverifiedLocalPolicyAsync();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(Endpoint, ManualFixtureRequest(evidenceCaptureRequested: false));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
            body.Should().NotBeNull();
            body!.DraftAccepted.Should().BeFalse();
            body.DraftPersisted.Should().BeFalse();
            body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_POLICY_UNVERIFIED");
            body.DraftId.Should().BeNull();

            var activeDraftCount = await CountActiveDraftsAsync(ManualFixtureParkingSessionId, "SENIOR_CITIZEN");
            activeDraftCount.Should().Be(0);
        }
        finally
        {
            await CleanupDraftLocalPolicyFixtureAsync();
        }
    }

    /// <summary>
    /// Verifies duplicate replay returns the original stored policy snapshot without overwriting it.
    /// </summary>
    [Fact]
    public async Task Draft_WhenDuplicateReplay_PreservesStoredPolicySnapshot()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await PrepareDraftPolicyFixtureAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var request = ManualFixtureRequest(evidenceCaptureRequested: false);

        using var firstResponse = await client.PostAsJsonAsync(Endpoint, request);
        var first = await firstResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        first.Should().NotBeNull();
        var firstStored = await ReadDraftPolicyContextAsync(first!.DraftId!.Value);

        using var secondResponse = await client.PostAsJsonAsync(Endpoint, request);
        var second = await secondResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        second.Should().NotBeNull();
        second!.ReusedExistingDraft.Should().BeTrue();
        second.DraftId.Should().Be(first.DraftId);

        var secondStored = await ReadDraftPolicyContextAsync(second.DraftId!.Value);
        secondStored.Should().NotBeNull();
        secondStored!.Snapshot.GetRawText().Should().Be(firstStored!.Snapshot.GetRawText());
        second.PolicySnapshot!.Value.GetProperty("resolvedAt").GetString()
            .Should().Be(firstStored.Snapshot.GetProperty("resolvedAt").GetString());
        second.PolicySnapshot!.Value.GetProperty("policyCode").GetString()
            .Should().Be(firstStored.Snapshot.GetProperty("policyCode").GetString());
    }

    /// <summary>
    /// Verifies session-not-found maps to 404 without draft persistence.
    /// </summary>
    [Fact]
    public async Task Draft_WhenSessionMissing_ReturnsNotFoundEnvelope()
    {
        using var factory = CreateFactory(NotFoundResult());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.AccessAllowed.Should().BeTrue();
        body.DraftAccepted.Should().BeFalse();
        body.DraftPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("SESSION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies validation errors map to Central PMS error envelopes.
    /// </summary>
    [Fact]
    public async Task Draft_WhenRequestInvalid_ReturnsBadRequest()
    {
        using var factory = CreateFactory(AcceptedResult(), throwValidation: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Endpoint, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DRAFT_REQUEST");
        body.CorrelationId.Should().Be(CorrelationId);
    }

    private static CustomWebApplicationFactory CreateFactory(
        OperatorConsoleStatutoryDiscountDraftResult result,
        bool throwValidation = false) =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleStatutoryDiscountDraftService>();
                services.AddSingleton<IOperatorConsoleStatutoryDiscountDraftService>(
                    new FakeStatutoryDiscountDraftService(result, throwValidation));
            });

    private static OperatorConsoleStatutoryDiscountDraftRequest Request() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId,
            "TICKET-001",
            PlateNumber: null,
            "SENIOR_CITIZEN",
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            "****1234",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: true,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            OperatorAttestation: true,
            AttestationNotes: "Manual operator attestation.",
            ReasonCode: "OPERATOR_DRAFT_REQUESTED",
            "operator-console-statutory-discount-draft-api-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftRequest ManualFixtureRequest(
        bool evidenceCaptureRequested = false,
        string entitlementType = "SENIOR_CITIZEN") =>
        new(
            Guid.Parse("77000000-0000-0000-0000-000000000010"),
            Guid.Parse("77000000-0000-0000-0000-000000000030"),
            Guid.Parse("77000000-0000-0000-0000-000000000002"),
            Guid.Parse("77000000-0000-0000-0000-000000000001"),
            Guid.Parse("77000000-0000-0000-0000-000000000050"),
            Guid.Parse("77000000-0000-0000-0000-000000000090"),
            "MANUAL-SESSION-LOOKUP-001",
            PlateNumber: null,
            entitlementType,
            entitlementType == "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID",
            entitlementType == "PWD" ? "NCDA" : "OSCA",
            ExpiryDate: null,
            entitlementType == "PWD" ? "PWD-UAT-****-0001" : "SC-UAT-****-0001",
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: evidenceCaptureRequested,
            EvidenceAccessIntent: null,
            OperatorAttestation: true,
            AttestationNotes: "Integration replay test draft only.",
            ReasonCode: "INTEGRATION_DUPLICATE_REPLAY",
            "operator-console-statutory-discount-draft-replay-test",
            Guid.NewGuid());

    private static OperatorConsoleStatutoryDiscountDraftResult DeniedResult() =>
        new(
            EvaluationId,
            AccessAllowed: false,
            "DENIED",
            ["NO_ACTIVE_SHIFT"],
            AccessPersisted: true,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            ValidationStatus: null,
            EvidenceCaptureRequired: true,
            EvidenceRequired: false,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            ReusedExistingDraft: false,
            Policy: null,
            IneligibilityReason: "ACCESS_DENIED",
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult AcceptedResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DraftAccepted: true,
            DraftPersisted: true,
            DraftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            EvidenceCaptureRequired: true,
            EvidenceRequired: true,
            EvidenceReferenceCreated: true,
            EvidenceReferenceId,
            ReusedExistingDraft: false,
            Policy(),
            IneligibilityReason: null,
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult NotFoundResult() =>
        new(
            EvaluationId,
            AccessAllowed: true,
            "ALLOWED",
            Array.Empty<string>(),
            AccessPersisted: true,
            DraftAccepted: false,
            DraftPersisted: false,
            DraftId: null,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            ValidationStatus: null,
            EvidenceCaptureRequired: true,
            EvidenceRequired: false,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null,
            ReusedExistingDraft: false,
            Policy: null,
            IneligibilityReason: "SESSION_NOT_FOUND",
            ErrorCode: "SESSION_NOT_FOUND",
            CorrelationId);

    private sealed class FakeStatutoryDiscountDraftService : IOperatorConsoleStatutoryDiscountDraftService
    {
        private readonly OperatorConsoleStatutoryDiscountDraftResult _result;
        private readonly bool _throwValidation;

        public FakeStatutoryDiscountDraftService(
            OperatorConsoleStatutoryDiscountDraftResult result,
            bool throwValidation)
        {
            _result = result;
            _throwValidation = throwValidation;
        }

        public Task<OperatorConsoleStatutoryDiscountDraftResult> DraftAsync(
            OperatorConsoleStatutoryDiscountDraftCommand command,
            CancellationToken cancellationToken)
        {
            if (_throwValidation)
            {
                throw new ArgumentException("EntitlementType is required.");
            }

            return Task.FromResult(_result);
        }
    }

    private static async Task SeedManualFixtureAsync()
    {
        await ClearPayableBasisApplyStateAsync();
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
        await PrepareDraftPolicyFixtureAsync();
    }

    private static async Task ClearPayableBasisApplyStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            UPDATE discounts.statutory_discount_validations
               SET tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
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
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = Guid.Parse("77000000-0000-0000-0000-000000000090");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PrepareDraftPolicyFixtureAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM discounts.discount_policy_references
            WHERE policy_code IN (
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'PH_RA10754_PWD_NATIONAL_FALLBACK',
                'INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY',
                'INTEGRATION_DRAFT_UNVERIFIED_LOCAL_POLICY'
            );

            DELETE FROM discounts.statutory_discount_policy_registry
            WHERE policy_code IN (
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'PH_RA10754_PWD_NATIONAL_FALLBACK',
                'INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY',
                'INTEGRATION_DRAFT_UNVERIFIED_LOCAL_POLICY'
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
                '6f000000-0000-0000-0000-000000000101',
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
                beneficiary_residency_scope,
                facility_scope,
                requires_evidence,
                required_evidence_type,
                requires_operator_validation,
                legal_basis_reference,
                national_law_reference,
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
                '6f000000-0000-0000-0000-000000000101',
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'RA 9994 Senior Citizen National Fallback',
                'Integration fallback policy.',
                'SENIOR_CITIZEN',
                'ACTIVE',
                'VERIFIED_OFFICIAL',
                'NATIONAL_LAW',
                'LEGAL_REFERENCE',
                'NATIONAL_LAW_FALLBACK',
                'STATUTORY_DISCOUNT_VAT_EXEMPT',
                'VAT_EXCLUSIVE',
                'PH',
                'Philippines',
                'NON_RESIDENT_ALLOWED',
                'Integration draft test fallback.',
                false,
                NULL,
                true,
                'RA 9994',
                'RA 9994',
                'integration-draft-v1',
                'integration-test-reviewer',
                now() - interval '2 days',
                'integration-test-approver',
                now() - interval '1 day',
                now() - interval '1 day',
                NULL,
                'Integration fallback policy.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                policy_level = EXCLUDED.policy_level,
                policy_type = EXCLUDED.policy_type,
                policy_resolution_basis = EXCLUDED.policy_resolution_basis,
                benefit_type = EXCLUDED.benefit_type,
                discount_base_scope = EXCLUDED.discount_base_scope,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                requires_evidence = EXCLUDED.requires_evidence,
                required_evidence_type = EXCLUDED.required_evidence_type,
                requires_operator_validation = EXCLUDED.requires_operator_validation,
                legal_basis_reference = EXCLUDED.legal_basis_reference,
                national_law_reference = EXCLUDED.national_law_reference,
                source_reference = EXCLUDED.source_reference,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                approved_by = EXCLUDED.approved_by,
                approved_at = EXCLUDED.approved_at,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
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
                '6f000000-0000-0000-0000-000000000102',
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
                beneficiary_residency_scope,
                facility_scope,
                requires_evidence,
                required_evidence_type,
                requires_operator_validation,
                legal_basis_reference,
                national_law_reference,
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
                '6f000000-0000-0000-0000-000000000102',
                'PH_RA10754_PWD_NATIONAL_FALLBACK',
                'RA 10754 PWD National Fallback',
                'Integration fallback policy.',
                'PWD',
                'ACTIVE',
                'VERIFIED_OFFICIAL',
                'NATIONAL_LAW',
                'LEGAL_REFERENCE',
                'NATIONAL_LAW_FALLBACK',
                'STATUTORY_DISCOUNT_VAT_EXEMPT',
                'VAT_EXCLUSIVE',
                'PH',
                'Philippines',
                'NON_RESIDENT_ALLOWED',
                'Integration draft test fallback.',
                false,
                NULL,
                true,
                'RA 10754',
                'RA 10754',
                'integration-draft-v1',
                'integration-test-reviewer',
                now() - interval '2 days',
                'integration-test-approver',
                now() - interval '1 day',
                now() - interval '1 day',
                NULL,
                'Integration fallback policy.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                policy_level = EXCLUDED.policy_level,
                policy_type = EXCLUDED.policy_type,
                policy_resolution_basis = EXCLUDED.policy_resolution_basis,
                benefit_type = EXCLUDED.benefit_type,
                discount_base_scope = EXCLUDED.discount_base_scope,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                requires_evidence = EXCLUDED.requires_evidence,
                required_evidence_type = EXCLUDED.required_evidence_type,
                requires_operator_validation = EXCLUDED.requires_operator_validation,
                legal_basis_reference = EXCLUDED.legal_basis_reference,
                national_law_reference = EXCLUDED.national_law_reference,
                source_reference = EXCLUDED.source_reference,
                reviewed_by = EXCLUDED.reviewed_by,
                reviewed_at = EXCLUDED.reviewed_at,
                approved_by = EXCLUDED.approved_by,
                approved_at = EXCLUDED.approved_at,
                effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to,
                updated_at = now();

            COMMIT;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = DraftPolicyLguCode;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = ManualFixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupDraftLocalPolicyFixtureAsync()
    {
        const string sql = """
            UPDATE discounts.discount_policy_references
               SET policy_status = 'DRAFT'::discounts.discount_policy_status_enum,
                   updated_at = now()
            WHERE policy_code IN (
                'INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY',
                'INTEGRATION_DRAFT_UNVERIFIED_LOCAL_POLICY'
            );

            UPDATE discounts.statutory_discount_policy_registry
               SET policy_status = 'DRAFT'::discounts.discount_policy_status_enum,
                   updated_at = now()
            WHERE policy_code IN (
                'INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY',
                'INTEGRATION_DRAFT_UNVERIFIED_LOCAL_POLICY'
            );

            UPDATE sites.sites
               SET lgu_code = 'PH-INT-DRAFT-CLEANED',
                   updated_at = now()
             WHERE site_id = @site_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = ManualFixtureSiteId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertDraftVerifiedLocalPolicyAsync()
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
                'INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY',
                'Integration Draft Verified Local Policy',
                'Integration test verified local draft policy.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'SENIOR_CITIZEN',
                'INTEGRATION-DRAFT-ORD-195',
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
                'INTEGRATION_DRAFT_VERIFIED_LOCAL_POLICY',
                'Integration Draft Verified Local Policy',
                'Integration test verified local draft policy.',
                'SENIOR_CITIZEN',
                'ACTIVE',
                'VERIFIED_OFFICIAL',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE_APPLIED',
                'STATUTORY_DISCOUNT_VAT_EXEMPT',
                'VAT_EXCLUSIVE',
                @lgu_code,
                'Integration Draft Jurisdiction',
                'NON_RESIDENT_ALLOWED',
                'Integration draft test local policy.',
                true,
                'SENIOR_CITIZEN_ID',
                true,
                'INTEGRATION-DRAFT-ORD-195',
                'INTEGRATION-DRAFT-ORD-195',
                'integration-draft-v1',
                'integration-test-reviewer',
                now() - interval '2 days',
                'integration-test-approver',
                now() - interval '1 day',
                now() - interval '1 day',
                NULL,
                'Integration verified local policy.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                policy_level = EXCLUDED.policy_level,
                policy_type = EXCLUDED.policy_type,
                policy_resolution_basis = EXCLUDED.policy_resolution_basis,
                benefit_type = EXCLUDED.benefit_type,
                discount_base_scope = EXCLUDED.discount_base_scope,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                requires_evidence = EXCLUDED.requires_evidence,
                required_evidence_type = EXCLUDED.required_evidence_type,
                requires_operator_validation = EXCLUDED.requires_operator_validation,
                legal_basis_reference = EXCLUDED.legal_basis_reference,
                ordinance_reference = EXCLUDED.ordinance_reference,
                source_reference = EXCLUDED.source_reference,
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
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = DraftVerifiedLocalPolicyId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = DraftPolicyLguCode;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertDraftUnverifiedLocalPolicyAsync()
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
                'INTEGRATION_DRAFT_UNVERIFIED_LOCAL_POLICY',
                'Integration Draft Unverified Local Policy',
                'Integration test unverified local draft policy.',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'SENIOR_CITIZEN',
                'INTEGRATION-DRAFT-UNVERIFIED-195',
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
                'INTEGRATION_DRAFT_UNVERIFIED_LOCAL_POLICY',
                'Integration Draft Unverified Local Policy',
                'Integration test unverified local draft policy.',
                'SENIOR_CITIZEN',
                'DRAFT',
                'PROPOSED_ONLY',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE',
                'LOCAL_ORDINANCE_APPLIED',
                'STATUTORY_DISCOUNT_VAT_EXEMPT',
                'VAT_EXCLUSIVE',
                @lgu_code,
                'Integration Draft Jurisdiction',
                'NON_RESIDENT_ALLOWED',
                'Integration draft test unverified policy.',
                true,
                'SENIOR_CITIZEN_ID',
                true,
                'INTEGRATION-DRAFT-UNVERIFIED-195',
                'INTEGRATION-DRAFT-UNVERIFIED-195',
                'integration-draft-v1',
                'integration-test-reviewer',
                now() - interval '2 days',
                NULL,
                NULL,
                now() - interval '1 day',
                NULL,
                'Integration unverified local policy.',
                gen_random_uuid()
            )
            ON CONFLICT (policy_code) DO UPDATE
            SET statutory_discount_policy_registry_id = EXCLUDED.statutory_discount_policy_registry_id,
                policy_name = EXCLUDED.policy_name,
                entitlement_type = EXCLUDED.entitlement_type,
                policy_status = EXCLUDED.policy_status,
                verification_status = EXCLUDED.verification_status,
                policy_level = EXCLUDED.policy_level,
                policy_type = EXCLUDED.policy_type,
                policy_resolution_basis = EXCLUDED.policy_resolution_basis,
                benefit_type = EXCLUDED.benefit_type,
                discount_base_scope = EXCLUDED.discount_base_scope,
                jurisdiction_code = EXCLUDED.jurisdiction_code,
                requires_evidence = EXCLUDED.requires_evidence,
                required_evidence_type = EXCLUDED.required_evidence_type,
                requires_operator_validation = EXCLUDED.requires_operator_validation,
                legal_basis_reference = EXCLUDED.legal_basis_reference,
                ordinance_reference = EXCLUDED.ordinance_reference,
                source_reference = EXCLUDED.source_reference,
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
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = DraftUnverifiedLocalPolicyId;
        command.Parameters.Add("lgu_code", NpgsqlDbType.Varchar).Value = DraftPolicyLguCode;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountActiveDraftsAsync(Guid parkingSessionId, string entitlementType)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
              AND validation_status IN (
                    'REQUESTED'::discounts.statutory_discount_validations_status_enum,
                    'PENDING_OPERATOR_REVIEW'::discounts.statutory_discount_validations_status_enum
              );
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = entitlementType;

        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count);
    }

    private static async Task<DraftPolicyContextRow?> ReadDraftPolicyContextAsync(Guid draftId)
    {
        const string sql = """
            SELECT
                COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id) AS statutory_discount_policy_id,
                p.policy_code,
                NULL::uuid AS resolved_jurisdiction_id,
                sdv.policy_resolution_basis::text,
                jsonb_build_object(
                    'statutoryDiscountPolicyId', COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id),
                    'policyCode', p.policy_code,
                    'nationalLawReference', p.national_law_reference,
                    'ordinanceReference', p.local_ordinance_reference,
                    'benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT',
                    'freeDurationMinutes', NULL,
                    'policyResolutionBasis', sdv.policy_resolution_basis::text,
                    'resolvedAt', sdv.requested_at
                )::text AS resolved_policy_snapshot_json
            FROM discounts.statutory_discount_validations sdv
            LEFT JOIN discounts.discount_policy_references p
              ON p.discount_policy_reference_id = COALESCE(
                    sdv.applied_policy_reference_id,
                    sdv.evaluated_policy_reference_id,
                    sdv.fallback_policy_reference_id)
            WHERE sdv.statutory_discount_validation_id = @draft_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = draftId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new DraftPolicyContextRow(
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            JsonDocument.Parse(reader.GetString(4)).RootElement.Clone());
    }

    private static async Task<int> CountEvidenceReferencesAsync(Guid draftId, string evidenceType)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND purged_at IS NULL;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;

        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt32(count);
    }

    private static async Task<EvidenceReferenceRow?> ReadEvidenceReferenceAsync(Guid draftId, string evidenceType)
    {
        const string sql = """
            SELECT
                der.discount_evidence_reference_id,
                der.evidence_storage_type::text,
                der.evidence_storage_ref,
                der.evidence_hash,
                der.evidence_capture_status::text,
                der.access_classification::text,
                der.redaction_status::text,
                sdv.evidence_captured
            FROM discounts.discount_evidence_references der
            JOIN discounts.statutory_discount_validations sdv
              ON sdv.statutory_discount_validation_id = der.statutory_discount_validation_id
            WHERE der.statutory_discount_validation_id = @statutory_discount_validation_id
              AND der.evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND der.purged_at IS NULL
            ORDER BY der.created_at DESC, der.discount_evidence_reference_id DESC
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new EvidenceReferenceRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetBoolean(7));
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

    private sealed record EvidenceReferenceRow(
        Guid EvidenceReferenceId,
        string EvidenceStorageType,
        string? EvidenceStorageRef,
        string? EvidenceHash,
        string EvidenceCaptureStatus,
        string AccessClassification,
        string RedactionStatus,
        bool EvidenceCaptured);

    private sealed record DraftPolicyContextRow(
        Guid? PolicyId,
        string? PolicyCode,
        Guid? JurisdictionId,
        string PolicyResolutionBasis,
        JsonElement Snapshot);

    private static OperatorConsoleResolvedStatutoryDiscountPolicy Policy() =>
        new(
            Guid.Parse("48000000-0000-0000-0000-000000000012"),
            Guid.Parse("48000000-0000-0000-0000-000000000013"),
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "RA 9994 Senior Citizen National Fallback",
            "NATIONAL_LAW_FALLBACK",
            "NATIONAL_LAW",
            "LEGAL_REFERENCE",
            "Expanded Senior Citizens Act of 2010",
            null,
            "RA 9994",
            "VERIFIED_OFFICIAL",
            "NON_RESIDENT_ALLOWED",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            null,
            false,
            false,
            false,
            false,
            false,
            false,
            "NOT_APPLICABLE",
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "CHARGEABLE_PORTION_ONLY",
            "NO_STACKING_ON_FREE_PERIOD",
            "NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY",
            true,
            true,
            DateOnly.Parse("2026-01-01"),
            null,
            "Integration test policy.",
            JsonSerializer.SerializeToElement(new
            {
                policyCode = "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
                nationalLawReference = "RA 9994",
                benefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT",
                freeDurationMinutes = (int?)null
            }));
}
