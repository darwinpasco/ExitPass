using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Application;

public sealed class AptPayableBasisReadinessStatutoryFacadeTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("41000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteGroupId = Guid.Parse("41000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("41000000-0000-0000-0000-000000000003");
    private static readonly Guid SitePosServerId = Guid.Parse("41000000-0000-0000-0000-000000000004");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("41000000-0000-0000-0000-000000000005");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("41000000-0000-0000-0000-000000000006");
    private static readonly Guid DecisionCommandId = Guid.Parse("41000000-0000-0000-0000-000000000007");
    private static readonly Guid ApplicationCommandId = Guid.Parse("41000000-0000-0000-0000-000000000008");
    private static readonly Guid ValidationId = Guid.Parse("41000000-0000-0000-0000-000000000009");
    private static readonly Guid CorrelationId = Guid.Parse("41000000-0000-0000-0000-000000000010");

    [Fact]
    public async Task Resolve_WhenNoStatutoryWorkflowActive_PreservesNonStatutoryReadiness()
    {
        var context = CreateContext();

        var result = await context.Sut.ResolveAsync(ResolveRequest(statutoryDecisionCommandId: null), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Applicable.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Ready.Should().BeTrue();
        result.Response.BlockingReasonCodes.Should().BeEmpty();
        context.StatutoryDiscounts.GetCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenDecisionAwaitingReview_BlocksCashReadinessWithoutMutation()
    {
        var context = CreateContext(AwaitingReview());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Applicable.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessStatus.Should().Be("AWAITING_REVIEW");
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessAction.Should().Be("POLL_READBACK");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_AWAITING_REVIEW");
        result.Response.BlockingReasonCodes.Should().NotContain("AMOUNT_CHANGED");
        context.StatutoryDiscounts.GetCallCount.Should().Be(1);
        context.StatutoryDiscounts.SubmitCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenDecisionApprovedApplicationNotRequested_BlocksWithSubmitApplicationIntentAction()
    {
        var context = CreateContext(ApprovedApplicationNotRequested());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.PayableBasisReadinessStatus
            .Should().Be("DECISION_APPROVED_APPLICATION_NOT_REQUESTED");
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessAction
            .Should().Be("SUBMIT_APPLICATION_INTENT");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_APPLICATION_NOT_REQUESTED");
        context.StatutoryDiscounts.SubmitCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenApplicationProcessing_BlocksWithCanonicalApplicationIdentity()
    {
        var context = CreateContext(ApplicationProcessing());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.StatutoryDiscountPayableBasisApplicationCommandId
            .Should().Be(ApplicationCommandId);
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessStatus.Should().Be("APPLICATION_PROCESSING");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_APPLICATION_PROCESSING");
        context.StatutoryDiscounts.SubmitCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenDecisionRejected_BlocksAsNonRetryable()
    {
        var context = CreateContext(Rejected());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Retryable.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessAction.Should().Be("DO_NOT_RETRY");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_DECISION_REJECTED");
    }

    [Fact]
    public async Task Resolve_WhenRetryableStatutoryFailure_PreservesRecoveryClassificationAndAction()
    {
        var context = CreateContext(RetryableApplicationFailure());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Retryable.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.RecoveryClassification
            .Should().Be("WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY");
        result.Response.StatutoryDiscountReadiness.RecoveryAction.Should().Be("WAIT_AND_RETRY");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_RETRYABLE_FAILURE");
    }

    [Fact]
    public async Task Resolve_WhenTerminalStatutoryFailure_BlocksAsSupportRequired()
    {
        var context = CreateContext(TerminalApplicationFailure());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Retryable.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.RecoveryAction.Should().Be("DO_NOT_RETRY");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_TERMINAL_FAILURE");
    }

    [Fact]
    public async Task Resolve_WhenAppliedFactsComplete_UsesAppliedSnapshotAndFinalAmount()
    {
        var context = CreateContext(Applied());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.TariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(8000);
        result.Response.Currency.Should().Be("PHP");
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Ready.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.OriginalTariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
        result.Response.StatutoryDiscountReadiness.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.Response.StatutoryDiscountReadiness.VatExclusiveBasisAmountMinorUnits.Should().Be(8929);
        result.Response.StatutoryDiscountReadiness.VatAmountMinorUnits.Should().Be(1071);
        result.Response.StatutoryDiscountReadiness.StatutoryDiscountAmountMinorUnits.Should().Be(2000);
        context.TerminalCashEligibility.LastRequest.Should().NotBeNull();
        context.TerminalCashEligibility.LastRequest!.TariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        context.TerminalCashEligibility.LastRequest.ExpectedAmountMinorUnits.Should().Be(8000);
    }

    [Fact]
    public async Task Resolve_WhenAppliedFactsMissFinalAmount_BlocksWithoutZeroSubstitution()
    {
        var context = CreateContext(Applied(netPayableAmountMinorUnits: null));

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.FinalPayableAmountMinorUnits.Should().BeNull();
        result.Response.StatutoryDiscountReadiness.BlockingReasonCode.Should().Be("STATUTORY_DISCOUNT_REQUIRED_FACTS_UNAVAILABLE");
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_REQUIRED_FACTS_UNAVAILABLE");
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(10000);
    }

    [Fact]
    public async Task Resolve_WhenAppliedFactsMissSnapshot_BlocksWithoutUsingFinalAmount()
    {
        var context = CreateContext(Applied(includeAppliedTariffSnapshot: false));

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.TariffSnapshotId.Should().Be(OriginalTariffSnapshotId);
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(10000);
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.AppliedTariffSnapshotId.Should().BeNull();
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_REQUIRED_FACTS_UNAVAILABLE");
    }

    [Fact]
    public async Task Resolve_WhenAppliedFactsMissCurrency_BlocksWithoutZeroSubstitution()
    {
        var context = CreateContext(Applied(currency: null));

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Currency.Should().BeNull();
        result.Response.StatutoryDiscountReadiness.FinalPayableAmountMinorUnits.Should().Be(8000);
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(10000);
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_REQUIRED_FACTS_UNAVAILABLE");
    }

    [Fact]
    public async Task Resolve_WhenParkingSessionMismatches_BlocksSafely()
    {
        var readback = Applied() with { ParkingSessionId = Guid.Parse("41000000-0000-0000-0000-000000000099") };
        var context = CreateContext(readback);

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_STATE_INCONSISTENT");
        result.Response.StatutoryDiscountReadiness!.Message.Should().Contain("does not match");
    }

    [Fact]
    public async Task Resolve_WhenSiteMismatches_BlocksSafely()
    {
        var readback = Applied() with { SiteId = Guid.Parse("41000000-0000-0000-0000-000000000098") };
        var context = CreateContext(readback);

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_STATE_INCONSISTENT");
    }

    [Fact]
    public async Task Revalidate_WhenAppliedBasisUnchanged_ReturnsPassedUnchanged()
    {
        var context = CreateContext(Applied());

        var result = await context.Sut.RevalidateAsync(
            RevalidateRequest(AppliedTariffSnapshotId, expectedAmountMinorUnits: 8000),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.RevalidationOutcome.Should().Be("PASSED_UNCHANGED");
        result.Response.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.TariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(8000);
    }

    [Fact]
    public async Task Revalidate_WhenAppliedAmountDiffersFromExpected_ReturnsAmountChangedWithStatutoryFacts()
    {
        var context = CreateContext(Applied(netPayableAmountMinorUnits: 7500));

        var result = await context.Sut.RevalidateAsync(
            RevalidateRequest(OriginalTariffSnapshotId, expectedAmountMinorUnits: 10000),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.RevalidationOutcome.Should().Be("AMOUNT_CHANGED");
        result.Response.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(7500);
        result.Response.TariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.FinalPayableAmountMinorUnits.Should().Be(7500);
        result.Response.BlockingReasonCodes.Should().Contain("AMOUNT_CHANGED");
    }

    [Fact]
    public async Task Revalidate_WhenStatutoryPending_DoesNotReportAmountChanged()
    {
        var context = CreateContext(AwaitingReview());

        var result = await context.Sut.RevalidateAsync(
            RevalidateRequest(OriginalTariffSnapshotId, expectedAmountMinorUnits: 9999),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.RevalidationOutcome.Should().Be("STATUTORY_DISCOUNT_BLOCKED");
        result.Response.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.BlockingReasonCodes.Should().Contain("STATUTORY_DISCOUNT_AWAITING_REVIEW");
        result.Response.BlockingReasonCodes.Should().NotContain("AMOUNT_CHANGED");
    }

    private static TestContext CreateContext(StatutoryDiscountDecisionResult? statutoryReadback = null)
    {
        var session = ParkingSession.Rehydrate(
            ParkingSessionId,
            SiteGroupId.ToString("D"),
            SiteId.ToString("D"),
            "FAKE-PMS",
            "VENDOR-SESSION-001",
            "TICKET",
            "ABC1234",
            "TICKET-001",
            DateTimeOffset.Parse("2026-07-28T00:00:00Z"),
            ParkingSessionStatus.PaymentRequired);
        var originalTariff = Tariff(OriginalTariffSnapshotId, 100m, 0m, 100m);
        var appliedTariff = Tariff(AppliedTariffSnapshotId, 100m, 20m, 80m, OriginalTariffSnapshotId);
        var terminalCashEligibility = new RecordingTerminalCashEligibilityReader();
        var statutoryDiscounts = new RecordingStatutoryDiscountFacadeService(statutoryReadback);

        return new TestContext(
            new AptPayableBasisReadinessService(
                new FixedVendorResolution(session, originalTariff),
                new FixedParkingSessionReadRepository(session),
                new FixedTariffSnapshotReadRepository(originalTariff, appliedTariff),
                terminalCashEligibility,
                new ReadySalesInvoiceProfileAdministrationService(),
                statutoryDiscounts),
            terminalCashEligibility,
            statutoryDiscounts);
    }

    private static AptPayableBasisResolveRequest ResolveRequest(Guid? statutoryDecisionCommandId) =>
        new(
            SiteGroupId.ToString("D"),
            SiteId.ToString("D"),
            SitePosServerId.ToString("D"),
            "APT-TERMINAL-001",
            "FAKE-PMS",
            "TICKET",
            "TICKET-001",
            PlateNumber: null,
            StatutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
            CorrelationId);

    private static AptPayableBasisRevalidateRequest RevalidateRequest(
        Guid tariffSnapshotId,
        long expectedAmountMinorUnits) =>
        new(
            ParkingSessionId.ToString("D"),
            tariffSnapshotId.ToString("D"),
            SiteGroupId.ToString("D"),
            SiteId.ToString("D"),
            SitePosServerId.ToString("D"),
            "APT-TERMINAL-001",
            "FAKE-PMS",
            "TICKET-001",
            PlateNumber: null,
            ExpectedAmountMinorUnits: expectedAmountMinorUnits,
            ExpectedCurrency: "PHP",
            StatutoryDiscountDecisionCommandId: DecisionCommandId,
            CorrelationId);

    private static TariffSnapshot Tariff(
        Guid tariffSnapshotId,
        decimal grossAmount,
        decimal statutoryDiscountAmount,
        decimal netPayable,
        Guid? supersedes = null) =>
        TariffSnapshot.Rehydrate(
            tariffSnapshotId,
            ParkingSessionId,
            statutoryDiscountAmount > 0 ? TariffSnapshotSourceType.StatutoryAdjusted : TariffSnapshotSourceType.Base,
            grossAmount,
            statutoryDiscountAmount,
            couponDiscountAmount: 0m,
            netPayable,
            "PHP",
            grossAmount,
            "v1",
            null,
            DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
            DateTimeOffset.UtcNow.AddMinutes(15),
            TariffSnapshotStatus.Active,
            supersedes,
            consumedByPaymentAttemptId: null);

    private static StatutoryDiscountDecisionResult AwaitingReview() =>
        Applied(
            decisionStatus: "AWAITING_REVIEW",
            decisionResultStatus: "NOT_DECIDED",
            decisionCommandStatus: StatutoryDiscountDecisionCommandStatuses.AwaitingReview,
            applicationRequested: false,
            applicationCommandStatus: StatutoryDiscountApplicationStageStatuses.NotRequested,
            payableBasisReady: false,
            payableBasisReadinessStatus: StatutoryDiscountPayableBasisReadinessStatuses.AwaitingReview,
            payableBasisReadinessAction: StatutoryDiscountDecisionRecoveryActions.PollReadback,
            appliedTariffSnapshotId: null,
            netPayableAmountMinorUnits: null);

    private static StatutoryDiscountDecisionResult ApprovedApplicationNotRequested() =>
        Applied(
            decisionStatus: "APPROVED",
            applicationRequested: false,
            applicationCommandStatus: StatutoryDiscountApplicationStageStatuses.NotRequested,
            payableBasisReady: false,
            payableBasisReadinessStatus: StatutoryDiscountPayableBasisReadinessStatuses.DecisionApprovedApplicationNotRequested,
            payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
            includeAppliedTariffSnapshot: false,
            netPayableAmountMinorUnits: null,
            currency: null);

    private static StatutoryDiscountDecisionResult ApplicationProcessing() =>
        Applied(
            decisionStatus: "APPROVED",
            applicationCommandStatus: StatutoryDiscountApplicationStageStatuses.Processing,
            payableBasisReady: false,
            payableBasisReadinessStatus: StatutoryDiscountPayableBasisReadinessStatuses.ApplicationProcessing,
            payableBasisReadinessAction: StatutoryDiscountDecisionRecoveryActions.PollReadback,
            includeAppliedTariffSnapshot: false,
            netPayableAmountMinorUnits: null);

    private static StatutoryDiscountDecisionResult Rejected() =>
        Applied(
            decisionStatus: "REJECTED",
            decisionResultStatus: "REJECTED",
            applicationRequested: false,
            applicationCommandStatus: StatutoryDiscountApplicationStageStatuses.NotRequested,
            payableBasisReady: false,
            payableBasisReadinessStatus: StatutoryDiscountPayableBasisReadinessStatuses.DecisionRejected,
            payableBasisReadinessAction: StatutoryDiscountDecisionRecoveryActions.DoNotRetry,
            includeAppliedTariffSnapshot: false,
            netPayableAmountMinorUnits: null,
            currency: null);

    private static StatutoryDiscountDecisionResult RetryableApplicationFailure() =>
        Applied(
            applicationCommandStatus: StatutoryDiscountApplicationStageStatuses.FailedRetryable,
            payableBasisReady: false,
            payableBasisReadinessStatus: StatutoryDiscountPayableBasisReadinessStatuses.RetryableFailure,
            payableBasisReadinessAction: StatutoryDiscountDecisionRecoveryActions.WaitAndRetry,
            includeAppliedTariffSnapshot: false,
            netPayableAmountMinorUnits: null,
            applicationRetryable: true,
            applicationRecoveryClassification: StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
            applicationRecoveryAction: StatutoryDiscountDecisionRecoveryActions.WaitAndRetry,
            errorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE");

    private static StatutoryDiscountDecisionResult TerminalApplicationFailure() =>
        Applied(
            applicationCommandStatus: StatutoryDiscountApplicationStageStatuses.FailedNonRetryable,
            payableBasisReady: false,
            payableBasisReadinessStatus: StatutoryDiscountPayableBasisReadinessStatuses.TerminalFailure,
            payableBasisReadinessAction: StatutoryDiscountDecisionRecoveryActions.DoNotRetry,
            includeAppliedTariffSnapshot: false,
            netPayableAmountMinorUnits: null,
            applicationRecoveryClassification: StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable,
            applicationRecoveryAction: StatutoryDiscountDecisionRecoveryActions.DoNotRetry,
            errorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_FAILED");

    private static StatutoryDiscountDecisionResult Applied(
        string decisionStatus = "APPLIED_PAYABLE_BASIS",
        string? decisionResultStatus = "APPROVED",
        string decisionCommandStatus = StatutoryDiscountDecisionCommandStatuses.Completed,
        bool applicationRequested = true,
        string applicationCommandStatus = StatutoryDiscountApplicationStageStatuses.Applied,
        bool payableBasisReady = true,
        string payableBasisReadinessStatus = StatutoryDiscountPayableBasisReadinessStatuses.PayableBasisReady,
        string? payableBasisReadinessAction = null,
        Guid? appliedTariffSnapshotId = null,
        bool includeAppliedTariffSnapshot = true,
        long? netPayableAmountMinorUnits = 8000,
        string? currency = "PHP",
        bool applicationRetryable = false,
        string applicationRecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.None,
        string? applicationRecoveryAction = null,
        string? errorCode = null) =>
        new(
            DecisionCommandId,
            RequestReference: Guid.Parse("41000000-0000-0000-0000-000000000013"),
            ValidationId,
            ParkingSessionId,
            StatutoryDiscountSourceChannels.AssistedPaymentTerminal,
            "SENIOR_CITIZEN",
            decisionStatus,
            PolicyResolutionBasis: "NATIONAL_RA9994",
            AppliedPolicyReferenceId: Guid.Parse("41000000-0000-0000-0000-000000000014"),
            FallbackPolicyReferenceId: null,
            LocalOrdinanceApplied: false,
            GrossAmountMinorUnits: 10000,
            StatutoryDiscountAmountMinorUnits: 2000,
            netPayableAmountMinorUnits,
            Currency: currency,
            EvidenceRequired: true,
            EvidenceRecorded: true,
            ReasonCode: null,
            ErrorCode: errorCode,
            CorrelationId,
            CreatedAt: DateTimeOffset.Parse("2026-07-28T00:00:00Z"),
            DecidedAt: DateTimeOffset.Parse("2026-07-28T00:02:00Z"),
            AppliedAt: applicationCommandStatus == StatutoryDiscountApplicationStageStatuses.Applied
                ? DateTimeOffset.Parse("2026-07-28T00:03:00Z")
                : null,
            OriginalTariffSnapshotId,
            includeAppliedTariffSnapshot ? appliedTariffSnapshotId ?? AppliedTariffSnapshotId : null,
            ResultClassification: "ACCEPTED",
            SemanticHashSourceVersion: StatutoryDiscountDecisionSemanticHash.SourceVersion,
            DecisionCommandStatus: decisionCommandStatus,
            DecisionResultStatus: decisionResultStatus,
            DecisionRetryable: false,
            DecisionRecoveryClassification: StatutoryDiscountDecisionRecoveryClassifications.None,
            DecisionRecoveryAction: null,
            StatutoryDiscountPayableBasisApplicationCommandId: applicationRequested ? ApplicationCommandId : null,
            ApplicationRequested: applicationRequested,
            ApplicationCommandStatus: applicationCommandStatus,
            ApplicationResultClassification: applicationCommandStatus,
            ApplicationSemanticHashSourceVersion: "statutory-discount-payable-basis-application:sha256:v1",
            ApplicationRetryable: applicationRetryable,
            ApplicationRecoveryClassification: applicationRecoveryClassification,
            ApplicationRecoveryAction: applicationRecoveryAction,
            OverallResultClassification: "DECISION_AND_APPLICATION_COMPLETED",
            OneShotComplete: true,
            SiteId,
            SiteGroupId,
            VatExclusiveBasisAmountMinorUnits: 8929,
            VatAmountMinorUnits: 1071,
            VatTreatment: "VAT_EXCLUSIVE",
            PayableBasisReady: payableBasisReady,
            PayableBasisReadinessStatus: payableBasisReadinessStatus,
            PayableBasisReadinessAction: payableBasisReadinessAction);

    private sealed record TestContext(
        AptPayableBasisReadinessService Sut,
        RecordingTerminalCashEligibilityReader TerminalCashEligibility,
        RecordingStatutoryDiscountFacadeService StatutoryDiscounts);

    private sealed class FixedVendorResolution : IResolveVendorParkingUseCase
    {
        private readonly ParkingSession _session;
        private readonly TariffSnapshot _tariff;

        public FixedVendorResolution(ParkingSession session, TariffSnapshot tariff)
        {
            _session = session;
            _tariff = tariff;
        }

        public Task<ResolveVendorParkingResult> ExecuteAsync(
            ResolveVendorParkingCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(ResolveVendorParkingResult.Resolved(
                _session,
                _tariff,
                CorrelationId,
                "FAKE-PMS",
                "Site Group",
                "Site",
                "Not Started",
                effectivePayableBasis: null));
    }

    private sealed class FixedParkingSessionReadRepository : IParkingSessionReadRepository
    {
        private readonly ParkingSession _session;

        public FixedParkingSessionReadRepository(ParkingSession session)
        {
            _session = session;
        }

        public Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken) =>
            Task.FromResult(parkingSessionId == _session.ParkingSessionId ? _session : null);
    }

    private sealed class FixedTariffSnapshotReadRepository : ITariffSnapshotReadRepository
    {
        private readonly IReadOnlyDictionary<Guid, TariffSnapshot> _tariffs;

        public FixedTariffSnapshotReadRepository(params TariffSnapshot[] tariffs)
        {
            _tariffs = tariffs.ToDictionary(tariff => tariff.TariffSnapshotId);
        }

        public Task<TariffSnapshot?> GetByIdAsync(Guid tariffSnapshotId, CancellationToken cancellationToken) =>
            Task.FromResult(_tariffs.TryGetValue(tariffSnapshotId, out var tariff) ? tariff : null);

        public Task<EffectiveTariffSnapshotResolution?> GetEffectiveAppliedTariffSnapshotAsync(
            Guid parkingSessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<EffectiveTariffSnapshotResolution?>(null);

        public Task<bool> WasConsumedOnlyByFailedPaymentAttemptAsync(
            Guid tariffSnapshotId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class RecordingTerminalCashEligibilityReader : ITerminalCashPayableBasisEligibilityReader
    {
        public TerminalCashPayableBasisEligibilityRequest? LastRequest { get; private set; }

        public Task<TerminalCashPayableBasisEligibility> EvaluateAsync(
            TerminalCashPayableBasisEligibilityRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new TerminalCashPayableBasisEligibility(
                true,
                AptPayableBasisReadinessStatuses.Ready,
                BlockingReasonCode: null,
                Retryable: false,
                Message: "Terminal cash is available."));
        }
    }

    private sealed class RecordingStatutoryDiscountFacadeService : IStatutoryDiscountDecisionFacadeService
    {
        private readonly StatutoryDiscountDecisionResult? _readback;

        public RecordingStatutoryDiscountFacadeService(StatutoryDiscountDecisionResult? readback)
        {
            _readback = readback;
        }

        public int GetCallCount { get; private set; }

        public int SubmitCallCount { get; private set; }

        public Task<StatutoryDiscountDecisionResult> SubmitAsync(
            StatutoryDiscountDecisionCommand command,
            CancellationToken cancellationToken)
        {
            SubmitCallCount++;
            throw new NotSupportedException("APT payable-basis facade tests must not submit statutory decisions.");
        }

        public Task<StatutoryDiscountParkingAvailabilityResult> ResolveAvailabilityAsync(
            StatutoryDiscountParkingAvailabilityRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("APT payable-basis facade tests must not resolve statutory parking availability.");

        public Task<StatutoryDiscountDecisionResult?> GetAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            GetCallCount++;
            return Task.FromResult(_readback);
        }
    }

    private sealed class ReadySalesInvoiceProfileAdministrationService : ISalesInvoiceProfileAdministrationService
    {
        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
            ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>.Success(
                new ManagementPlatformSalesInvoiceHeaderProfileReadiness(
                    SiteId,
                    SitePosServerId,
                    DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
                    ManagementPlatformSalesInvoiceProfileReadinessStatuses.Ready,
                    Guid.Parse("41000000-0000-0000-0000-000000000011"),
                    1,
                    Guid.Parse("41000000-0000-0000-0000-000000000012"),
                    ManagementPlatformSalesInvoiceProfileLifecycleStates.Approved,
                    true,
                    true,
                    [],
                    "VALID",
                    "COMPLETE",
                    "SUPPORTED",
                    "NO_OVERLAP",
                    DateTimeOffset.Parse("2026-07-28T00:00:00Z"),
                    CorrelationId),
                CorrelationId,
                200));

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
            ManagementPlatformFiscalIdentityMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
            Guid fiscalIdentityId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
            Guid fiscalIdentityId,
            ManagementPlatformFiscalIdentityMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
            ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
            ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
            Guid salesInvoiceHeaderProfileId,
            ManagementPlatformPosServerAdminRequestContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
