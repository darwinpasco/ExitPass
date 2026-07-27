using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
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
    private static readonly Guid FixtureReviewerUserId = Guid.Parse("77000000-0000-0000-0000-000000000012");
    private static readonly Guid FixtureParkingSessionId = Guid.Parse("4c000000-0000-0000-0000-000000000090");
    private static readonly Guid FixtureOriginalTariffSnapshotId = Guid.Parse("4c000000-0000-0000-0000-000000000091");
    private static readonly Guid FixtureVendorSystemId = Guid.Parse("77000000-0000-0000-0000-000000000004");
    private static readonly Guid FixtureServiceIdentityId = Guid.Parse("77000000-0000-0000-0000-000000000003");
    private static readonly Guid NoEvidencePolicyId = Guid.Parse("6f000000-0000-0000-0000-000000000101");
    private static readonly Guid MissingPolicyContextValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000195");
    private static readonly Guid EvaluatedOnlyPolicyContextValidationId = Guid.Parse("4c000000-0000-0000-0000-000000000196");
    private static readonly Guid PaymentAttemptGuardrailValidationId = Guid.Parse("4c000000-0000-0000-0000-00000000019a");
    private static readonly Guid AlreadyDiscountedGuardrailValidationId = Guid.Parse("4c000000-0000-0000-0000-00000000019b");
    private static readonly Guid CouponStackingGuardrailValidationId = Guid.Parse("4c000000-0000-0000-0000-00000000019c");
    private static readonly Guid ExpiredTariffGuardrailValidationId = Guid.Parse("4c000000-0000-0000-0000-00000000019d");
    private static readonly Guid StaleTariffGuardrailValidationId = Guid.Parse("4c000000-0000-0000-0000-00000000019e");

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
        using var factory = CreateAllowedAccessFactory();
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
        decision.StatutoryDiscountDecisionCommandId.Should().NotBeNull();

        var applyRequest = ApplyRequest(originalTariffSnapshotId: null);
        using var applyResponse = await client.PostAsJsonAsync(
            ApplyEndpoint(draft.DraftId.Value),
            applyRequest);
        var applyResponseBody = await applyResponse.Content.ReadAsStringAsync();

        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK, applyResponseBody);
        var applied = await applyResponse.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        applied.Should().NotBeNull();
        applied!.AccessAllowed.Should().BeTrue();
        applied.ApplicationAccepted.Should().BeTrue();
        applied.ApplicationPersisted.Should().BeTrue();
        applied.PayableBasisApplicationId.Should().NotBeNull();
        applied.StatutoryDiscountDecisionCommandId.Should().Be(decision.StatutoryDiscountDecisionCommandId);
        applied.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
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
        applied.ResolvedJurisdictionId.Should().BeNull();
        applied.PolicyResolutionBasis.Should().Be("NATIONAL_LAW_FALLBACK");
        applied.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        applied.BenefitType.Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        applied.NationalLawReference.Should().Be("RA 9994");
        applied.OrdinanceReference.Should().BeNull();
        applied.PolicySnapshotUsed.Should().BeTrue();

        var rowCount = await CountApplicationsAsync(draft.DraftId.Value);
        rowCount.Should().Be(1);
        var application = await ReadApplicationAsync(draft.DraftId.Value);
        application.ApplicationId.Should().Be(applied.PayableBasisApplicationId!.Value);
        application.ValidationId.Should().Be(draft.DraftId.Value);
        application.ParkingSessionId.Should().Be(FixtureParkingSessionId);
        application.OriginalTariffSnapshotId.Should().Be(FixtureOriginalTariffSnapshotId);
        application.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        application.ApplicationStatus.Should().Be("APPLIED");
        application.GrossAmountMinorUnits.Should().Be(12500);
        application.VatExclusiveAmountMinorUnits.Should().Be(11161);
        application.VatAmountMinorUnits.Should().Be(1339);
        application.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        application.FinalPayableAmountMinorUnits.Should().Be(8929);
        application.IdempotencyKey.Should().StartWith("operator-console-payable-basis-application-v1:sha256:");
        var applicationCommand = await ReadApplicationCommandAsync(decision.StatutoryDiscountDecisionCommandId!.Value);
        applicationCommand.ApplicationCommandId.Should().Be(applied.StatutoryDiscountPayableBasisApplicationCommandId!.Value);
        applicationCommand.PayableBasisApplicationId.Should().Be(applied.PayableBasisApplicationId!.Value);
        applicationCommand.CommandStatus.Should().Be("APPLIED");
        applicationCommand.ResultClassification.Should().Be("APPLIED");
        applicationCommand.IdempotencyKey.Should().StartWith("operator-console-payable-basis-application-v1:sha256:");
        applicationCommand.SourceChannel.Should().Be("OPERATOR_CONSOLE");
        application.AppliedByUserId.Should().Be(FixtureUserId);
        var computationBasis = await ReadApplicationComputationBasisAsync(draft.DraftId.Value);
        computationBasis.GetProperty("policyContext").GetProperty("policyCode").GetString()
            .Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        computationBasis.GetProperty("policyContext").GetProperty("benefitType").GetString()
            .Should().Be("STATUTORY_DISCOUNT_VAT_EXEMPT");
        computationBasis.GetProperty("policyContext").GetProperty("nationalLawReference").GetString()
            .Should().Be("RA 9994");
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
        replay.StatutoryDiscountDecisionCommandId.Should().Be(decision.StatutoryDiscountDecisionCommandId);
        replay.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(applied.StatutoryDiscountPayableBasisApplicationCommandId);
        replay.AppliedTariffSnapshotId.Should().Be(applied.AppliedTariffSnapshotId);
        replay.ApplicationStatus.Should().Be("APPLIED");
        replay.PolicySnapshotUsed.Should().BeTrue();
        replay.PolicyCode.Should().Be(applied.PolicyCode);
        (await CountApplicationsAsync(draft.DraftId.Value)).Should().Be(1);
        (await CountApplicationCommandsForDecisionAsync(decision.StatutoryDiscountDecisionCommandId.Value)).Should().Be(1);
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
        await InsertApprovedValidationAsync(MissingPolicyContextValidationId, policyId: null);
        await InsertApprovedCanonicalDecisionCommandAsync(MissingPolicyContextValidationId, policyId: null);

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
    /// Verifies validations with only an evaluated policy reference resolve through the locked policy-reference columns.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenOnlyEvaluatedPolicyReferenceExists_AppliesUsingPolicyReferenceContext()
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
            EvaluatedOnlyPolicyContextValidationId,
            NoEvidencePolicyId,
            includeAppliedPolicyReference: false);
        await InsertApprovedCanonicalDecisionCommandAsync(EvaluatedOnlyPolicyContextValidationId, NoEvidencePolicyId);

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(EvaluatedOnlyPolicyContextValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeTrue();
        body.ApplicationPersisted.Should().BeTrue();
        body.StatutoryDiscountPolicyId.Should().Be(NoEvidencePolicyId);
        body.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        body.PolicySnapshotUsed.Should().BeTrue();
        (await CountApplicationsAsync(EvaluatedOnlyPolicyContextValidationId)).Should().Be(1);
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
            NoEvidencePolicyId);
        await InsertApprovedCanonicalDecisionCommandAsync(PaymentAttemptGuardrailValidationId, NoEvidencePolicyId);
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

    /// <summary>
    /// Verifies an already-discounted original payable basis cannot receive another statutory discount.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenOriginalAlreadyHasStatutoryDiscount_FailsClosed()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync(statutoryDiscountAmount: 5.00m, netAmount: 120.00m);
        await InsertApprovedValidationAsync(AlreadyDiscountedGuardrailValidationId, NoEvidencePolicyId);
        await InsertApprovedCanonicalDecisionCommandAsync(AlreadyDiscountedGuardrailValidationId, NoEvidencePolicyId);

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(AlreadyDiscountedGuardrailValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("STATUTORY_DISCOUNT_ALREADY_APPLIED");
        (await CountAppliedTariffSnapshotsAsync(AlreadyDiscountedGuardrailValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies statutory discounts do not stack with an existing coupon/discount composition.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenOriginalHasCouponDiscount_FailsClosed()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync(couponDiscountAmount: 5.00m, netAmount: 120.00m);
        await InsertApprovedValidationAsync(CouponStackingGuardrailValidationId, NoEvidencePolicyId);
        await InsertApprovedCanonicalDecisionCommandAsync(CouponStackingGuardrailValidationId, NoEvidencePolicyId);

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(CouponStackingGuardrailValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.IneligibilityReason.Should().Be("COUPON_COMPOSITION_NOT_SUPPORTED");
        body.ErrorCode.Should().Be("PAYABLE_BASIS_COMPONENTS_MISSING");
        (await CountAppliedTariffSnapshotsAsync(CouponStackingGuardrailValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies expired original tariff snapshots are rejected and no applied snapshot is created.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenOriginalTariffExpired_FailsClosed()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await InsertApprovedValidationAsync(ExpiredTariffGuardrailValidationId, NoEvidencePolicyId);
        await InsertApprovedCanonicalDecisionCommandAsync(ExpiredTariffGuardrailValidationId, NoEvidencePolicyId);

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(ExpiredTariffGuardrailValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("SESSION_NOT_ELIGIBLE");
        (await CountAppliedTariffSnapshotsAsync(ExpiredTariffGuardrailValidationId)).Should().Be(0);
    }

    /// <summary>
    /// Verifies stale/non-active original tariff snapshots are rejected.
    /// </summary>
    [Fact]
    public async Task ApplyPayableBasis_WhenOriginalTariffStale_FailsClosed()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        await SeedManualFixtureAsync();
        await ResetFixtureApplyStateAsync();
        await InsertParkingSessionAsync();
        await InsertBaseTariffSnapshotAsync(snapshotStatus: "SUPERSEDED");
        await InsertApprovedValidationAsync(StaleTariffGuardrailValidationId, NoEvidencePolicyId);
        await InsertApprovedCanonicalDecisionCommandAsync(StaleTariffGuardrailValidationId, NoEvidencePolicyId);

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            ApplyEndpoint(StaleTariffGuardrailValidationId),
            ApplyRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>();
        body.Should().NotBeNull();
        body!.ApplicationAccepted.Should().BeFalse();
        body.ApplicationPersisted.Should().BeFalse();
        body.ErrorCode.Should().Be("TARIFF_SNAPSHOT_NOT_FOUND");
        (await CountAppliedTariffSnapshotsAsync(StaleTariffGuardrailValidationId)).Should().Be(0);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftResponse> CreateDraftAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(DraftEndpoint, DraftRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDraftResponse>();
        body.Should().NotBeNull();
        body!.DraftAccepted.Should().BeTrue(
            "draft should be accepted; error={0} ineligibility={1} readiness={2} reason={3}",
            body.ErrorCode,
            body.IneligibilityReason,
            body.PolicyReadinessClassification,
            body.PolicyReadinessReason);
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
            FixtureReviewerUserId,
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

    private static CustomWebApplicationFactory CreateAllowedAccessFactory() =>
        new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IOperatorConsoleAccessEvaluationService>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.AddSingleton<IOperatorConsoleAccessEvaluationService>(new FakeAllowedAccessEvaluationService());
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakeAllowedAccessEvaluationWriter());
            });

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

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisRequest ApplyRequest() =>
        ApplyRequest(FixtureOriginalTariffSnapshotId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisRequest ApplyRequest(Guid? originalTariffSnapshotId) =>
        new(
            FixtureUserId,
            FixtureDeviceBindingId,
            FixtureSiteId,
            FixtureSiteGroupId,
            FixtureShiftId,
            originalTariffSnapshotId,
            $"operator-console-statutory-discount-apply-{Guid.NewGuid():N}",
            Guid.NewGuid());

    private static string DecisionEndpoint(Guid draftId) => string.Format(DecisionEndpointTemplate, draftId);

    private static string ApplyEndpoint(Guid validationId) => string.Format(ApplyEndpointTemplate, validationId);

    private static async Task SeedManualFixtureAsync()
    {
        await ApplyCanonicalDecisionConvergencePatchesAsync();
        await ClearPayableBasisApplyStateAsync();
        await OperatorConsoleStatutoryDiscountLockedSchemaFixture.SeedAsync(OpenConnectionAsync);
        await InsertNoEvidenceNationalFallbackPolicyAsync();
    }

    private static async Task ApplyCanonicalDecisionConvergencePatchesAsync()
    {
        await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(
            CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
    }

    private static async Task InsertNoEvidenceNationalFallbackPolicyAsync()
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
                @policy_id,
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'RA 9994 Senior Citizen National Fallback',
                'Focused integration fixture for statutory discount payable-basis computation.',
                'SENIOR_CITIZEN'::discounts.statutory_entitlement_type_enum,
                'ACTIVE'::discounts.discount_policy_status_enum,
                'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum,
                'NATIONAL_LAW'::discounts.discount_policy_level_enum,
                'LEGAL_REFERENCE'::discounts.discount_policy_type_enum,
                'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum,
                'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum,
                'VAT_EXCLUSIVE'::discounts.discount_base_scope_enum,
                'PH',
                'Philippines',
                NULL,
                NULL,
                'NON_RESIDENT_ALLOWED'::discounts.beneficiary_residency_scope_enum,
                'Operator Console apply-payable-basis fixture only.',
                false,
                NULL,
                true,
                'RA 9994',
                NULL,
                'RA 9994',
                'central-pms-statutory-discount-computation-contract',
                'central-pms-test',
                now() - interval '2 days',
                'central-pms-test',
                now() - interval '1 day',
                DATE '2026-01-01',
                NULL,
                'Focused non-production integration fixture.',
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
                requires_evidence = EXCLUDED.requires_evidence,
                requires_operator_validation = EXCLUDED.requires_operator_validation,
                legal_basis_reference = EXCLUDED.legal_basis_reference,
                national_law_reference = EXCLUDED.national_law_reference,
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
                local_ordinance_reference,
                national_law_reference,
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
                'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
                'RA 9994 Senior Citizen National Fallback',
                'Focused integration fixture for statutory discount payable-basis computation.',
                'LEGAL_REFERENCE',
                'NATIONAL_LAW',
                'SENIOR_CITIZEN',
                NULL,
                'RA 9994',
                NULL,
                10,
                'integration-v1',
                true,
                false,
                now() - interval '1 day',
                'ACTIVE'
            )
            ON CONFLICT (policy_code, policy_version) DO UPDATE
            SET lgu_code = EXCLUDED.lgu_code,
                national_law_reference = EXCLUDED.national_law_reference,
                requires_evidence_capture = EXCLUDED.requires_evidence_capture,
                policy_status = EXCLUDED.policy_status,
                updated_at = now();
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = NoEvidencePolicyId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearPayableBasisApplyStateAsync()
    {
        const string sql = """
            BEGIN;
            SET CONSTRAINTS ALL DEFERRED;

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

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
            );

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            UPDATE discounts.statutory_discount_validations
               SET tariff_snapshot_id = NULL
             WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;

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

    private static async Task InsertBaseTariffSnapshotAsync(
        decimal grossAmount = 125.00m,
        decimal statutoryDiscountAmount = 0.00m,
        decimal couponDiscountAmount = 0.00m,
        decimal? netAmount = null,
        string snapshotStatus = "ACTIVE",
        DateTimeOffset? expiresAt = null)
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
                @gross_amount,
                @statutory_discount_amount,
                @coupon_discount_amount,
                @net_amount,
                CAST(@snapshot_status AS core.tariff_snapshot_status_enum),
                now(),
                @expires_at,
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
        command.Parameters.Add("gross_amount", NpgsqlDbType.Numeric).Value = grossAmount;
        command.Parameters.Add("statutory_discount_amount", NpgsqlDbType.Numeric).Value = statutoryDiscountAmount;
        command.Parameters.Add("coupon_discount_amount", NpgsqlDbType.Numeric).Value = couponDiscountAmount;
        command.Parameters.Add("net_amount", NpgsqlDbType.Numeric).Value =
            netAmount ?? grossAmount - statutoryDiscountAmount - couponDiscountAmount;
        command.Parameters.Add("snapshot_status", NpgsqlDbType.Varchar).Value = snapshotStatus;
        command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1);
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
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
            SELECT computation_basis_json::text
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND application_status = 'APPLIED'::discounts.statutory_discount_payable_application_status_enum;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;

        var json = (string?)await command.ExecuteScalarAsync();
        json.Should().NotBeNull();
        return JsonDocument.Parse(json!).RootElement.Clone();
    }

    private static async Task<ApplicationRow> ReadApplicationAsync(Guid validationId)
    {
        const string sql = """
            SELECT
                statutory_discount_payable_basis_application_id,
                statutory_discount_validation_id,
                parking_session_id,
                original_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                application_status::text,
                gross_amount_minor_units,
                vat_amount_minor_units,
                vat_exclusive_amount_minor_units,
                statutory_discount_amount_minor_units,
                final_payable_amount_minor_units,
                idempotency_key,
                applied_by_user_id
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected statutory discount payable-basis application fixture row was not found.");
        }

        return new ApplicationRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }

    private static async Task<ApplicationCommandRow> ReadApplicationCommandAsync(Guid statutoryDiscountDecisionCommandId)
    {
        const string sql = """
            SELECT
                statutory_discount_payable_basis_application_command_id,
                statutory_discount_decision_command_id,
                statutory_discount_payable_basis_application_id,
                business_identity,
                semantic_hash_source_version,
                semantic_request_hash,
                idempotency_scope,
                idempotency_key,
                source_channel,
                command_status::text,
                result_classification::text,
                retryable,
                safe_error_code
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected statutory discount payable-basis application command fixture row was not found.");
        }

        return new ApplicationCommandRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static async Task InsertApprovedValidationAsync(
        Guid validationId,
        Guid? policyId,
        bool includeAppliedPolicyReference = true)
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
                evaluated_policy_reference_id,
                applied_policy_reference_id
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
                @applied_policy_id
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
        command.Parameters.Add("applied_policy_id", NpgsqlDbType.Uuid).Value =
            includeAppliedPolicyReference && policyId.HasValue ? policyId.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertApprovedCanonicalDecisionCommandAsync(Guid validationId, Guid? policyId)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_decision_commands (
                request_reference,
                parking_session_id,
                source_channel,
                entitlement_type,
                idempotency_scope,
                idempotency_key,
                semantic_request_hash,
                semantic_hash_source_version,
                statutory_discount_validation_id,
                original_tariff_snapshot_id,
                decision_status,
                result_classification,
                policy_resolution_basis,
                applied_policy_reference_id,
                local_ordinance_applied,
                evidence_required,
                evidence_recorded,
                reason_code,
                original_correlation_id,
                decided_at,
                completed_at,
                updated_at,
                business_identity,
                command_status,
                decision_result_status,
                retryable,
                recovery_classification
            )
            SELECT
                gen_random_uuid(),
                sdv.parking_session_id,
                'OPERATOR_CONSOLE',
                sdv.entitlement_type::text,
                'statutory-discount-decision:' || sdv.parking_session_id::text || ':' || sdv.entitlement_type::text,
                'operator-console-apply-fixture-decision-' || replace(sdv.statutory_discount_validation_id::text, '-', ''),
                'sha256:' || encode(sha256(convert_to('operator-console-apply-fixture-decision:' || sdv.statutory_discount_validation_id::text, 'UTF8')), 'hex'),
                'statutory-discount-decision:sha256:v2',
                sdv.statutory_discount_validation_id,
                sdv.tariff_snapshot_id,
                'APPROVED',
                'ACCEPTED',
                sdv.policy_resolution_basis::text,
                @policy_id,
                sdv.local_ordinance_applied,
                sdv.evidence_required,
                sdv.evidence_captured,
                sdv.decision_reason_code,
                sdv.correlation_id,
                now(),
                now(),
                now(),
                'statutory-discount-decision:' || sdv.parking_session_id::text || ':' || sdv.entitlement_type::text,
                'COMPLETED',
                'APPROVED',
                false,
                'READ_CANONICAL_RESULT'
            FROM discounts.statutory_discount_validations AS sdv
            WHERE sdv.statutory_discount_validation_id = @validation_id;
            """;

        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("policy_id", NpgsqlDbType.Uuid).Value = policyId.HasValue ? policyId.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync();
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

    private sealed record ApplicationRow(
        Guid ApplicationId,
        Guid ValidationId,
        Guid ParkingSessionId,
        Guid OriginalTariffSnapshotId,
        Guid? AppliedTariffSnapshotId,
        string ApplicationStatus,
        long GrossAmountMinorUnits,
        long VatAmountMinorUnits,
        long VatExclusiveAmountMinorUnits,
        long StatutoryDiscountAmountMinorUnits,
        long FinalPayableAmountMinorUnits,
        string? IdempotencyKey,
        Guid? AppliedByUserId);

    private sealed record ApplicationCommandRow(
        Guid ApplicationCommandId,
        Guid DecisionCommandId,
        Guid? PayableBasisApplicationId,
        string BusinessIdentity,
        string SemanticHashSourceVersion,
        string SemanticHashValue,
        string IdempotencyScope,
        string IdempotencyKey,
        string SourceChannel,
        string CommandStatus,
        string ResultClassification,
        bool Retryable,
        string? SafeErrorCode);

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
