using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Application.WebPay;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
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
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("41000000-0000-0000-0000-000000000011");
    private static readonly Guid PosServerFiscalDocumentId = Guid.Parse("41000000-0000-0000-0000-000000000012");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("41000000-0000-0000-0000-000000000015");

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
    public async Task Resolve_WhenDecisionAwaitingReview_AllowsRegularCashWithoutMutation()
    {
        var context = CreateContext(AwaitingReview());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Applicable.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessStatus.Should().Be("AWAITING_REVIEW");
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessAction.Should().Be("POLL_READBACK");
        result.Response.BlockingReasonCodes.Should().NotContain("STATUTORY_DISCOUNT_AWAITING_REVIEW");
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
    public async Task Resolve_WhenApprovedOperatorReviewWasNotApplied_ContinuesZeroPayableApplication()
    {
        var applied = Applied(
            sourceChannel: StatutoryDiscountSourceChannels.OperatorConsole,
            netPayableAmountMinorUnits: 0,
            statutoryDiscountAmountMinorUnits: 10000);
        var context = CreateContext(
            ApprovedApplicationNotRequested(StatutoryDiscountSourceChannels.OperatorConsole),
            submittedReadback: applied,
            statutoryReview: ApprovedOperatorReview(),
            zeroPayableAppliedTariff: true);
        var cashier = new AptZeroPayableCompletionActor(
            Guid.Parse("41000000-0000-0000-0000-000000000016"),
            "Mara Del Carmen",
            "apt-pitx-level-3-01");

        var result = await context.Sut.ResolveAsync(
            ResolveRequest(DecisionCommandId),
            cashier,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(0);
        result.Response.StatutoryDiscountReadiness!.PayableBasisReadinessStatus
            .Should().Be(StatutoryDiscountPayableBasisReadinessStatuses.PayableBasisReady);
        context.StatutoryDiscounts.SubmitCallCount.Should().Be(1);
        context.StatutoryDiscounts.LastSubmittedCommand.Should().NotBeNull();
        context.StatutoryDiscounts.LastSubmittedCommand!.ApplyPayableBasis.Should().BeTrue();
        context.StatutoryDiscounts.LastSubmittedCommand.IdempotencyKey
            .Should().Be($"operator-console-review-auto-apply:{DecisionCommandId:N}");
        context.StatutoryDiscounts.CompletionActor.Should().Be(cashier);
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
    public async Task Resolve_WhenDecisionRejected_AllowsRegularCashAsNonRetryable()
    {
        var context = CreateContext(Rejected());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.Retryable.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessAction.Should().Be("DO_NOT_RETRY");
        result.Response.BlockingReasonCodes.Should().NotContain("STATUTORY_DISCOUNT_DECISION_REJECTED");
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
        var context = CreateContext(Applied(
            originalVatAmountMinorUnits: 1071,
            finalVatableSalesMinorUnits: 6929,
            finalVatAmountMinorUnits: 1071));

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
        result.Response.StatutoryDiscountReadiness.OriginalVatAmountMinorUnits.Should().Be(1071);
        result.Response.StatutoryDiscountReadiness.FinalVatableSalesMinorUnits.Should().Be(6929);
        result.Response.StatutoryDiscountReadiness.FinalVatAmountMinorUnits.Should().Be(1071);
        context.TerminalCashEligibility.LastRequest.Should().NotBeNull();
        context.TerminalCashEligibility.LastRequest!.TariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        context.TerminalCashEligibility.LastRequest.ExpectedAmountMinorUnits.Should().Be(8000);
    }

    [Fact]
    public async Task Resolve_WhenFullFeeExemptionApplied_ForwardsAuthoritativeZeroVatFacts()
    {
        var context = CreateContext(
            Applied(
                grossAmountMinorUnits: 5000,
                vatExclusiveBasisAmountMinorUnits: 4464,
                vatAmountMinorUnits: 536,
                statutoryDiscountAmountMinorUnits: 4464,
                netPayableAmountMinorUnits: 0,
                originalVatAmountMinorUnits: 536,
                finalVatableSalesMinorUnits: 0,
                finalVatAmountMinorUnits: 0),
            zeroPayableAppliedTariff: true);

        var result = await context.Sut.ResolveAsync(
            ResolveRequest(DecisionCommandId),
            new AptZeroPayableCompletionActor(
                Guid.Parse("41000000-0000-0000-0000-000000000016"),
                "Mara Del Carmen",
                "apt-pitx-level-3-01"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.OriginalAmountMinorUnits.Should().Be(5000);
        result.Response.StatutoryDiscountReadiness.VatExclusiveBasisAmountMinorUnits.Should().Be(4464);
        result.Response.StatutoryDiscountReadiness.OriginalVatAmountMinorUnits.Should().Be(536);
        result.Response.StatutoryDiscountReadiness.StatutoryDiscountAmountMinorUnits.Should().Be(4464);
        result.Response.StatutoryDiscountReadiness.FinalVatableSalesMinorUnits.Should().Be(0);
        result.Response.StatutoryDiscountReadiness.FinalVatAmountMinorUnits.Should().Be(0);
        result.Response.StatutoryDiscountReadiness.FinalPayableAmountMinorUnits.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_WhenAuthoritativeVatFactsAreUnavailable_PreservesNulls()
    {
        var context = CreateContext(Applied());

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response!.StatutoryDiscountReadiness!.OriginalVatAmountMinorUnits.Should().BeNull();
        result.Response.StatutoryDiscountReadiness.FinalVatableSalesMinorUnits.Should().BeNull();
        result.Response.StatutoryDiscountReadiness.FinalVatAmountMinorUnits.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_WhenZeroPayableCompletionExists_ReturnsCanonicalFiscalAndExitSummary()
    {
        var issuedAt = DateTimeOffset.Parse("2026-07-28T00:04:00Z");
        var readback = Applied(
            netPayableAmountMinorUnits: 0,
            statutoryDiscountAmountMinorUnits: 10000) with
        {
            ZeroPayableFiscalCompletion = new ZeroPayableStatutoryFiscalIssuanceResult(
                FiscalIssuanceReferenceId,
                FiscalPrerequisiteSatisfied: true,
                PosServerCallAttempted: true,
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                PosServerFiscalDocumentId,
                FiscalDocumentNumber: "SI-00000073",
                ElectronicJournalEventReference: "EJ-00000073",
                CompletionBasis: "ZERO_PAYABLE_STATUTORY_FINALITY",
                CompletionAuthorityReferenceId: ApplicationCommandId,
                SafeErrorCode: null),
            ExitAuthorization = new StatutoryDiscountExitAuthorizationReadback(
                ExitAuthorizationId,
                ParkingSessionId,
                AppliedTariffSnapshotId,
                CompletionBasis: "ZERO_PAYABLE_STATUTORY_FINALITY",
                CompletionAuthorityReferenceId: ApplicationCommandId,
                PaymentAttemptId: null,
                PaymentConfirmationId: null,
                AuthorizationStatus: "ISSUED",
                IssuedAt: issuedAt,
                ExpirationTimestamp: issuedAt.AddMinutes(15))
        };
        var context = CreateContext(readback, zeroPayableAppliedTariff: true);

        var cashierId = Guid.NewGuid();
        var result = await context.Sut.ResolveAsync(
            ResolveRequest(DecisionCommandId),
            new AptZeroPayableCompletionActor(cashierId, "Mara Del Carmen", "apt-pitx-level-3-01"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.AuthoritativeAmountMinorUnits.Should().Be(0);
        result.Response.ZeroPayableStatutoryCompletion.Should().NotBeNull();
        result.Response.ZeroPayableStatutoryCompletion!.CompletionBasis.Should().Be("ZERO_PAYABLE_STATUTORY_FINALITY");
        result.Response.ZeroPayableStatutoryCompletion.FiscalPrerequisiteSatisfied.Should().BeTrue();
        result.Response.ZeroPayableStatutoryCompletion.FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        result.Response.ZeroPayableStatutoryCompletion.PosServerFiscalDocumentId.Should().Be(PosServerFiscalDocumentId);
        result.Response.ZeroPayableStatutoryCompletion.FiscalDocumentNumber.Should().Be("SI-00000073");
        result.Response.ZeroPayableStatutoryCompletion.ExitAuthorizationId.Should().Be(ExitAuthorizationId);
        result.Response.ZeroPayableStatutoryCompletion.ExitAuthorizationStatus.Should().Be("ISSUED");
        context.StatutoryDiscounts.CompletionActor.Should().Be(
            new AptZeroPayableCompletionActor(cashierId, "Mara Del Carmen", "apt-pitx-level-3-01"));
    }

    [Fact]
    public async Task Resolve_WhenZeroPayableAndAptHumanSessionIsUnavailable_BlocksCompletion()
    {
        var readback = Applied(
            netPayableAmountMinorUnits: 0,
            statutoryDiscountAmountMinorUnits: 10000);
        var context = CreateContext(readback, zeroPayableAppliedTariff: true);

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.BlockingReasonCode.Should().Be("APT_HUMAN_SESSION_REQUIRED");
        result.Response.StatutoryDiscountReadiness.SafeErrorCode.Should().Be("APT_ZERO_PAYABLE_CASHIER_REQUIRED");
        context.StatutoryDiscounts.CompletionActor.Should().BeNull();
    }

    [Theory]
    [InlineData("REQUIRED_NOT_STARTED", "STATUTORY_EVIDENCE_REQUIRED_NOT_STARTED", false, false)]
    [InlineData("ITEM_CREATED", "STATUTORY_EVIDENCE_NOT_READY", false, false)]
    [InlineData("UPLOAD_SESSION_AVAILABLE", "STATUTORY_EVIDENCE_UPLOAD_PENDING", false, false)]
    [InlineData("UPLOAD_IN_PROGRESS", "STATUTORY_EVIDENCE_UPLOAD_PENDING", false, false)]
    [InlineData("VALIDATION_PENDING", "STATUTORY_EVIDENCE_VALIDATION_PENDING", false, false)]
    [InlineData("VALIDATION_FAILED", "STATUTORY_EVIDENCE_VALIDATION_FAILED", false, false)]
    [InlineData("SCAN_PENDING", "STATUTORY_EVIDENCE_SCAN_PENDING", false, false)]
    [InlineData("SCAN_RETRYABLE", "STATUTORY_EVIDENCE_SCAN_RETRYABLE", false, true)]
    [InlineData("SCAN_FAILED", "STATUTORY_EVIDENCE_SCAN_FAILED", false, false)]
    [InlineData("MALWARE_DETECTED", "STATUTORY_EVIDENCE_MALWARE_DETECTED", false, false)]
    [InlineData("NOT_REVIEWABLE", "STATUTORY_EVIDENCE_NOT_READY", false, false)]
    [InlineData("REVIEWABLE", "STATUTORY_EVIDENCE_NOT_READY", false, false)]
    [InlineData("REVIEW_PENDING", "STATUTORY_EVIDENCE_REVIEW_PENDING", false, false)]
    [InlineData("APPROVED", "STATUTORY_EVIDENCE_APPROVED_NOT_APPLIED", false, false)]
    [InlineData("REJECTED", "STATUTORY_EVIDENCE_REJECTED", false, false)]
    [InlineData("UNKNOWN_FAIL_CLOSED", "STATUTORY_EVIDENCE_CONTEXT_UNAVAILABLE", false, false)]
    [InlineData("NOT_REQUIRED", null, true, false)]
    [InlineData("APPLIED", null, true, false)]
    public async Task Resolve_CombinesCanonicalEvidenceReadinessWithEveryExistingCashDimension(
        string classification,
        string? blockingReason,
        bool evidenceReady,
        bool retryable)
    {
        var evidence = new StatutoryEvidenceChannelReadiness(
            classification,
            classification != "NOT_REQUIRED",
            evidenceReady,
            retryable,
            blockingReason,
            evidenceReady ? "Statutory evidence readiness passed." : "Statutory evidence is not ready for cash acceptance.");
        var context = CreateContext(Applied(), evidenceReadiness: evidence);

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().Be(evidenceReady);
        var dimension = result.Response.ReadinessDimensions.Single(value => value.Name == "statutoryEvidenceReadiness");
        dimension.Ready.Should().Be(evidenceReady);
        dimension.Status.Should().Be(evidenceReady ? AptPayableBasisReadinessStatuses.Ready : AptPayableBasisReadinessStatuses.Blocked);
        dimension.BlockingReasonCode.Should().Be(blockingReason);
        dimension.Retryable.Should().Be(retryable);
        if (!evidenceReady)
        {
            result.Response.BlockingReasonCodes.Should().Contain(blockingReason!);
        }
    }

    [Fact]
    public async Task Resolve_WhenVendorRediscoveryReturnsActiveAppliedSnapshot_RemainsCashReady()
    {
        var context = CreateContext(Applied(), vendorReturnsAppliedTariff: true);

        var result = await context.Sut.ResolveAsync(ResolveRequest(DecisionCommandId), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.ReadyForCashAcceptance.Should().BeTrue();
        result.Response.TariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.Response.AuthoritativeAmountMinorUnits.Should().Be(8000);
        result.Response.StatutoryDiscountReadiness!.Ready.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.BlockingReasonCode.Should().BeNull();
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
    public async Task Revalidate_WithExplicitTicket_DoesNotReplaceItWithStoredPlate()
    {
        var context = CreateContext(Applied());

        var result = await context.Sut.RevalidateAsync(
            RevalidateRequest(AppliedTariffSnapshotId, expectedAmountMinorUnits: 8000),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        context.VendorResolution.LastCommand.Should().NotBeNull();
        context.VendorResolution.LastCommand!.TicketReference.Should().Be("TICKET-001");
        context.VendorResolution.LastCommand.PlateNumber.Should().BeNull();
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
    public async Task Revalidate_WhenStatutoryPending_UsesRegularBasisAndReportsAmountChange()
    {
        var context = CreateContext(AwaitingReview());

        var result = await context.Sut.RevalidateAsync(
            RevalidateRequest(OriginalTariffSnapshotId, expectedAmountMinorUnits: 9999),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.RevalidationOutcome.Should().Be("AMOUNT_CHANGED");
        result.Response.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.BlockingReasonCodes.Should().NotContain("STATUTORY_DISCOUNT_AWAITING_REVIEW");
        result.Response.BlockingReasonCodes.Should().Contain("AMOUNT_CHANGED");
    }

    [Fact]
    public async Task Resolve_WhenCanonicalDecisionExists_DiscoversWorkflowAndCustomerInformationPresence()
    {
        var context = CreateContext(
            AwaitingReview(),
            rediscoveredDecisionCommandId: DecisionCommandId,
            customerInformationStatus: InvoiceCustomerInformationReadStatus.Found);

        var result = await context.Sut.ResolveAsync(ResolveRequest(statutoryDecisionCommandId: null), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.CustomerInformationSubmitted.Should().BeTrue();
        result.Response.StatutoryDiscountReadiness.Should().NotBeNull();
        result.Response.StatutoryDiscountReadiness!.StatutoryDiscountDecisionCommandId.Should().Be(DecisionCommandId);
        result.Response.StatutoryDiscountReadiness.PayableBasisReadinessStatus.Should().Be("AWAITING_REVIEW");
        result.Response.ReadyForCashAcceptance.Should().BeTrue();
        context.StatutoryDiscounts.GetCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_WhenProjectionSessionFound_ReturnsSessionWithoutCashAuthority()
    {
        var now = DateTimeOffset.Parse("2026-10-05T08:05:00+08:00");
        var projection = new VendorSessionProjection(
            Guid.Parse("455bfa51-98b3-4fbf-9efb-336339596a34"),
            Guid.Parse("45a625de-9034-4fb6-b527-0950d384e51f"),
            SiteId,
            SiteGroupId,
            "LOT-1",
            "PITX Level 3",
            null,
            null,
            null,
            null,
            null,
            "REC-3147",
            "1474119573147",
            "ABC1147",
            now.AddHours(-1),
            null,
            "TEMP",
            "ALLOW",
            null,
            "projection-test",
            new string('a', 64),
            "REC-3147",
            now.AddMinutes(-1),
            "VENDOR_RECORD_GUID",
            "HIKCENTRAL|GUID|REC-3147",
            now.AddMinutes(-1),
            now.AddMinutes(-1),
            now.AddSeconds(-20),
            VendorSessionProjectionStatus.Active,
            CorrelationId,
            now.AddMinutes(-1),
            now.AddSeconds(-20));
        var lookup = VendorSessionProjectionLookupResult.FoundProjection(
            projection,
            now.AddSeconds(-20),
            now,
            CorrelationId);
        var projectionResult = ResolveVendorParkingResult.ProjectionSession(
            lookup,
            "VENDOR_PMS_ADAPTER_ERROR_HIKCENTRAL_CODE_401",
            CorrelationId,
            projection.VendorSystemId!.Value.ToString("D"));
        var context = CreateContext(vendorResolutionResult: projectionResult);

        var result = await context.Sut.ResolveAsync(ResolveRequest(statutoryDecisionCommandId: null), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.HttpStatusCode.Should().Be(200);
        result.Response.Should().NotBeNull();
        result.Response!.SessionFound.Should().BeTrue();
        result.Response.SessionSource.Should().Be("VENDOR_SESSION_PROJECTION");
        result.Response.Degraded.Should().BeTrue();
        result.Response.PayableBasisAvailable.Should().BeFalse();
        result.Response.TicketReference.Should().Be("1474119573147");
        result.Response.PlateNumber.Should().Be("ABC1147");
        result.Response.ParkingSessionId.Should().BeNull();
        result.Response.TariffSnapshotId.Should().BeNull();
        result.Response.AuthoritativeAmountMinorUnits.Should().BeNull();
        result.Response.Currency.Should().BeNull();
        result.Response.PaymentStatus.Should().BeNull();
        result.Response.ReadyForCashAcceptance.Should().BeFalse();
        result.Response.BlockingReasonCodes.Should().Contain("PROJECTION_SESSION_PAYABLE_BASIS_UNAVAILABLE");
        context.TerminalCashEligibility.LastRequest.Should().BeNull();
        context.StatutoryDiscounts.GetCallCount.Should().Be(0);
    }

    private static TestContext CreateContext(
        StatutoryDiscountDecisionResult? statutoryReadback = null,
        bool vendorReturnsAppliedTariff = false,
        StatutoryEvidenceChannelReadiness? evidenceReadiness = null,
        Guid? rediscoveredDecisionCommandId = null,
        InvoiceCustomerInformationReadStatus customerInformationStatus = InvoiceCustomerInformationReadStatus.NotSupplied,
        ResolveVendorParkingResult? vendorResolutionResult = null,
        bool zeroPayableAppliedTariff = false,
        StatutoryDiscountDecisionResult? submittedReadback = null,
        StatutoryDiscountServiceChannelReviewDetail? statutoryReview = null)
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
        var appliedTariff = zeroPayableAppliedTariff
            ? Tariff(AppliedTariffSnapshotId, 100m, 100m, 0m, OriginalTariffSnapshotId)
            : Tariff(AppliedTariffSnapshotId, 100m, 20m, 80m, OriginalTariffSnapshotId);
        var terminalCashEligibility = new RecordingTerminalCashEligibilityReader();
        var statutoryDiscounts = new RecordingStatutoryDiscountFacadeService(statutoryReadback, submittedReadback);

        var vendorResolution = new FixedVendorResolution(
            session,
            vendorReturnsAppliedTariff ? appliedTariff : originalTariff,
            vendorResolutionResult);
        return new TestContext(
            new AptPayableBasisReadinessService(
                vendorResolution,
                new FixedParkingSessionReadRepository(session),
                new FixedTariffSnapshotReadRepository(originalTariff, appliedTariff),
                terminalCashEligibility,
                new ReadySalesInvoiceProfileAdministrationService(),
                statutoryDiscounts,
                new FixedStatutoryEvidenceChannelService(evidenceReadiness),
                new FixedStatutoryLifecycleRediscoveryService(rediscoveredDecisionCommandId),
                new FixedInvoiceCustomerInformationService(customerInformationStatus),
                statutoryReview is null ? null : new FixedStatutoryReviewRepository(statutoryReview)),
            terminalCashEligibility,
            statutoryDiscounts,
            vendorResolution);
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

    private static StatutoryDiscountDecisionResult ApprovedApplicationNotRequested(
        string sourceChannel = StatutoryDiscountSourceChannels.WebPay) =>
        Applied(
            sourceChannel: sourceChannel,
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

    private static StatutoryDiscountServiceChannelReviewDetail ApprovedOperatorReview() =>
        new(
            DecisionCommandId,
            ValidationId,
            Guid.Parse("41000000-0000-0000-0000-000000000013"),
            ParkingSessionId,
            StatutoryDiscountSourceChannels.OperatorConsole,
            SiteId,
            SiteGroupId,
            "TICKET-001",
            "ABC1234",
            "SENIOR_CITIZEN",
            StatutoryDiscountDecisionCommandStatuses.Completed,
            StatutoryDiscountDecisionV2ResultStates.Approved,
            StatutoryDiscountServiceChannelReviewStatuses.Approved,
            "SENIOR_CITIZEN_ID",
            "Paranaque City",
            ExpiryDate: null,
            "SC-****-0001",
            EvidenceReferences: [],
            RequesterAttestation: true,
            AttestationNotes: "Reviewed evidence accepted.",
            ReasonCode: "LGU_FULL_WAIVER",
            EvidenceRequired: true,
            EvidenceRecorded: true,
            OriginalTariffSnapshotId,
            OriginalAmountMinorUnits: 10000,
            VatExclusiveAmountMinorUnits: 8929,
            VatAmountMinorUnits: 1071,
            StatutoryDiscountAmountMinorUnits: 10000,
            FinalPayableAmountMinorUnits: 0,
            Currency: "PHP",
            GoverningPolicy: null,
            ReviewerUserId: Guid.Parse("41000000-0000-0000-0000-000000000017"),
            ReviewerAccessEvaluationId: Guid.Parse("41000000-0000-0000-0000-000000000018"),
            ReviewerDecision: "APPROVE",
            ReviewerReasonCode: "LGU_FULL_WAIVER",
            SubmittedAt: DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
            ReviewedAt: DateTimeOffset.Parse("2026-07-28T00:02:00Z"),
            PayableBasisApplicationStatus: null,
            CorrelationId)
        {
            IdControlReference = "SC-ID-0001",
            BirthDate = DateOnly.Parse("1950-01-01")
        };

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
        string sourceChannel = StatutoryDiscountSourceChannels.WebPay,
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
        string? errorCode = null,
        long statutoryDiscountAmountMinorUnits = 2000,
        long grossAmountMinorUnits = 10000,
        long vatExclusiveBasisAmountMinorUnits = 8929,
        long vatAmountMinorUnits = 1071,
        long? originalVatAmountMinorUnits = null,
        long? finalVatableSalesMinorUnits = null,
        long? finalVatAmountMinorUnits = null) =>
        new(
            DecisionCommandId,
            RequestReference: Guid.Parse("41000000-0000-0000-0000-000000000013"),
            ValidationId,
            ParkingSessionId,
            sourceChannel,
            "SENIOR_CITIZEN",
            decisionStatus,
            PolicyResolutionBasis: "NATIONAL_RA9994",
            AppliedPolicyReferenceId: Guid.Parse("41000000-0000-0000-0000-000000000014"),
            FallbackPolicyReferenceId: null,
            LocalOrdinanceApplied: false,
            GrossAmountMinorUnits: grossAmountMinorUnits,
            StatutoryDiscountAmountMinorUnits: statutoryDiscountAmountMinorUnits,
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
            VatExclusiveBasisAmountMinorUnits: vatExclusiveBasisAmountMinorUnits,
            VatAmountMinorUnits: vatAmountMinorUnits,
            VatTreatment: "VAT_EXCLUSIVE",
            PayableBasisReady: payableBasisReady,
            PayableBasisReadinessStatus: payableBasisReadinessStatus,
            PayableBasisReadinessAction: payableBasisReadinessAction,
            OriginalVatAmountMinorUnits: originalVatAmountMinorUnits,
            FinalVatableSalesMinorUnits: finalVatableSalesMinorUnits,
            FinalVatAmountMinorUnits: finalVatAmountMinorUnits);

    private sealed record TestContext(
        AptPayableBasisReadinessService Sut,
        RecordingTerminalCashEligibilityReader TerminalCashEligibility,
        RecordingStatutoryDiscountFacadeService StatutoryDiscounts,
        FixedVendorResolution VendorResolution);

    private sealed class FixedVendorResolution : IResolveVendorParkingUseCase
    {
        private readonly ParkingSession _session;
        private readonly TariffSnapshot _tariff;
        private readonly ResolveVendorParkingResult? _result;

        public FixedVendorResolution(
            ParkingSession session,
            TariffSnapshot tariff,
            ResolveVendorParkingResult? result = null)
        {
            _session = session;
            _tariff = tariff;
            _result = result;
        }

        public ResolveVendorParkingCommand? LastCommand { get; private set; }

        public Task<ResolveVendorParkingResult> ExecuteAsync(
            ResolveVendorParkingCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(_result ?? ResolveVendorParkingResult.Resolved(
                _session,
                _tariff,
                CorrelationId,
                "FAKE-PMS",
                "Site Group",
                "Site",
                "Not Started",
                effectivePayableBasis: null));
        }
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

        public Task<ParkingSessionLookupResult> FindByTicketReferenceAsync(
            Guid siteGroupId,
            Guid siteId,
            string? vendorSystemId,
            string ticketReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParkingSessionLookupResult(ParkingSessionLookupStatus.NotFound, null));

        public Task<ParkingSessionLookupResult> FindByPlateNumberAsync(
            Guid siteGroupId,
            Guid siteId,
            string? vendorSystemId,
            string plateNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParkingSessionLookupResult(ParkingSessionLookupStatus.NotFound, null));
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
        private StatutoryDiscountDecisionResult? _readback;
        private readonly StatutoryDiscountDecisionResult? _submittedReadback;

        public RecordingStatutoryDiscountFacadeService(
            StatutoryDiscountDecisionResult? readback,
            StatutoryDiscountDecisionResult? submittedReadback)
        {
            _readback = readback;
            _submittedReadback = submittedReadback;
        }

        public int GetCallCount { get; private set; }

        public int SubmitCallCount { get; private set; }

        public StatutoryDiscountDecisionCommand? LastSubmittedCommand { get; private set; }

        public AptZeroPayableCompletionActor? CompletionActor { get; private set; }

        public Task<StatutoryDiscountDecisionResult> SubmitAsync(
            StatutoryDiscountDecisionCommand command,
            CancellationToken cancellationToken)
        {
            SubmitCallCount++;
            LastSubmittedCommand = command;
            _readback = _submittedReadback ?? throw new NotSupportedException(
                "APT payable-basis facade test did not configure a submission result.");
            return Task.FromResult(_readback);
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

        public Task<StatutoryDiscountDecisionResult?> CompleteZeroPayableForAptAsync(
            Guid statutoryDiscountDecisionCommandId,
            AptZeroPayableCompletionActor actor,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            CompletionActor = actor;
            return Task.FromResult(_readback);
        }
    }

    private sealed class FixedStatutoryReviewRepository(StatutoryDiscountServiceChannelReviewDetail review)
        : IStatutoryDiscountServiceChannelReviewRepository
    {
        public Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StatutoryDiscountServiceChannelReviewDetail?>(
                statutoryDiscountDecisionCommandId == review.StatutoryDiscountDecisionCommandId ? review : null);

        public Task UpsertIntakeAsync(
            StatutoryDiscountServiceChannelReviewIntakeCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(
            StatutoryDiscountServiceChannelReviewQueueQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryDiscountServiceChannelValidationLinkage?> EnsureApprovedValidationLinkageAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid reviewerUserId,
            string? decisionReasonCode,
            StatutoryDiscountServiceChannelReviewedDocument reviewedDocument,
            Guid correlationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryDiscountServiceChannelReviewerAuthority?> GetValidationReviewerAuthorityAsync(
            Guid statutoryDiscountValidationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StatutoryDiscountServiceChannelReviewDetail> RecordReviewCompletionAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid reviewerUserId,
            Guid? operatorDeviceBindingId,
            Guid? operatorShiftId,
            Guid accessEvaluationId,
            string decision,
            string? decisionReasonCode,
            StatutoryDiscountServiceChannelReviewedDocument reviewedDocument,
            Guid correlationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedStatutoryEvidenceChannelService : IStatutoryEvidenceChannelService
    {
        private readonly StatutoryEvidenceChannelReadiness _readiness;

        public FixedStatutoryEvidenceChannelService(StatutoryEvidenceChannelReadiness? readiness) =>
            _readiness = readiness ?? new StatutoryEvidenceChannelReadiness("NOT_REQUIRED", false, true, false, null, "No statutory evidence is required.");

        public Task<StatutoryEvidenceChannelResponse> BootstrapAsync(StatutoryEvidenceChannelBootstrapCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StatutoryEvidenceChannelResponse> GetStatusAsync(StatutoryEvidenceChannelStatusQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StatutoryEvidenceChannelReadiness> GetAptEvidenceReadinessAsync(Guid? statutoryDiscountDecisionCommandId, StatutoryEvidenceActor actor, Guid correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(_readiness);

        public Task<StatutoryEvidenceOpaqueUploadSessionResponse> CreateUploadSessionAsync(StatutoryEvidenceChannelUploadSessionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StatutoryEvidenceOpaqueUploadSessionResponse> UploadAsync(StatutoryEvidenceChannelUploadCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StatutoryEvidenceChannelResponse> FinalizeUploadSessionAsync(StatutoryEvidenceChannelFinalizeCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ExitPass.CentralPms.Application.OperatorConsole.OperatorConsoleStatutoryEvidencePreviewResult> OpenPreviewAsync(
            StatutoryEvidenceChannelPreviewCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordPreviewStreamOutcomeAsync(
            ExitPass.CentralPms.Application.OperatorConsole.OperatorConsoleStatutoryEvidencePreviewAuditContext context,
            string outcome,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedStatutoryLifecycleRediscoveryService(Guid? decisionCommandId)
        : IWebPayStatutoryDiscountPendingLifecycleRediscoveryService
    {
        public Task<WebPayStatutoryDiscountPendingLifecycleRediscoveryResult> RediscoverAsync(
            WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
            CancellationToken cancellationToken)
        {
            if (decisionCommandId is null)
            {
                return Task.FromResult(WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle,
                    query.CorrelationId,
                    "No active lifecycle."));
            }

            return Task.FromResult(WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.Found(
                new WebPayStatutoryDiscountPendingLifecycleRecord(
                    Guid.Parse("41000000-0000-0000-0000-000000000013"),
                    decisionCommandId.Value,
                    Guid.Parse("41000000-0000-0000-0000-000000000014"),
                    "SENIOR_CITIZEN",
                    "AWAITING_REVIEW",
                    "AWAITING_REVIEW",
                    ParkingSessionId,
                    SiteId,
                    SiteGroupId,
                    "opaque-continuation",
                    null,
                    "PENDING_REVIEW",
                    true,
                    DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
                    DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
                    DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
                    null,
                    null),
                query.CorrelationId));
        }
    }

    private sealed class FixedInvoiceCustomerInformationService(InvoiceCustomerInformationReadStatus status)
        : IParkingSessionInvoiceCustomerInformationService
    {
        public Task<InvoiceCustomerInformationReadResult> ReadAsync(
            Guid parkingSessionId,
            InvoiceCustomerInformationScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(new InvoiceCustomerInformationReadResult(
                status,
                status == InvoiceCustomerInformationReadStatus.Found
                    ? new ParkingSessionInvoiceCustomerInformationRecord(
                        parkingSessionId,
                        "Private customer",
                        "Private address",
                        "Private TIN",
                        "Private business style",
                        1,
                        DateTimeOffset.Parse("2026-07-28T00:01:00Z"),
                        DateTimeOffset.Parse("2026-07-28T00:01:00Z"))
                    : null));

        public Task<InvoiceCustomerInformationSaveResult> SaveAsync(
            SaveParkingSessionInvoiceCustomerInformationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
