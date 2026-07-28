using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountDecisionFacadeServiceTests
{
    private static readonly Guid CommandId = Guid.Parse("6d000000-0000-0000-0000-000000000001");
    private static readonly Guid RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("6d000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("6d000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("6d000000-0000-0000-0000-000000000005");
    private static readonly Guid ActorUserId = Guid.Parse("6d000000-0000-0000-0000-000000000006");
    private static readonly Guid ReviewerUserId = Guid.Parse("6d000000-0000-0000-0000-000000000007");
    private static readonly Guid DeviceBindingId = Guid.Parse("6d000000-0000-0000-0000-000000000008");
    private static readonly Guid ShiftId = Guid.Parse("6d000000-0000-0000-0000-000000000009");
    private static readonly Guid ValidationId = Guid.Parse("6d000000-0000-0000-0000-00000000000a");
    private static readonly Guid PolicyId = Guid.Parse("6d000000-0000-0000-0000-00000000000b");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("6d000000-0000-0000-0000-00000000000c");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("6d000000-0000-0000-0000-00000000000d");
    private static readonly Guid PayableBasisApplicationId = Guid.Parse("6d000000-0000-0000-0000-00000000000e");
    private static readonly Guid CorrelationId = Guid.Parse("6d000000-0000-0000-0000-00000000000f");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-21T08:00:00Z");

    [Fact]
    public async Task SubmitAsync_WhenRequestIsValid_ReusesExistingOperatorConsoleWorkflow()
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(), CancellationToken.None);

        result.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        result.StatutoryDiscountValidationId.Should().Be(ValidationId);
        result.SourceChannel.Should().Be("OPERATOR_CONSOLE");
        result.DecisionStatus.Should().Be("APPLIED_PAYABLE_BASIS");
        result.GrossAmountMinorUnits.Should().Be(12500);
        result.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        result.NetPayableAmountMinorUnits.Should().Be(8929);
        result.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);

        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.EvidenceService.Received(1).CaptureAsync(Arg.Any<OperatorConsoleStatutoryDiscountEvidenceCaptureCommand>(), Arg.Any<CancellationToken>());
        await fixture.DecisionService.Received(1).DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("OPERATOR_CONSOLE")]
    [InlineData("WEBPAY")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL")]
    public async Task SubmitAsync_AttributesSupportedSourceChannelsWithoutChangingCalculationAuthority(string sourceChannel)
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: sourceChannel, applyPayableBasis: false), CancellationToken.None);

        result.SourceChannel.Should().Be(sourceChannel);
        fixture.Repository.LastDecisionCommand!.SourceChannel.Should().Be(sourceChannel);
        if (sourceChannel == "OPERATOR_CONSOLE")
        {
            result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.Completed);
            await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        }
        else
        {
            result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
            result.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
            result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
            await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyAndSemanticRequest_ReplaysOriginalResult()
    {
        var fixture = CreateFixture();
        var command = Command();

        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);
        var second = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceChannelChangesSourceChannel_ReplaysPendingReviewResult()
    {
        var fixture = CreateFixture();
        var command = Command(sourceChannel: "WEBPAY", applyPayableBasis: false);
        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var second = await fixture.Sut.SubmitAsync(command with { SourceChannel = "ASSISTED_PAYMENT_TERMINAL" }, CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.SourceChannel.Should().Be("WEBPAY");
        second.ResultClassification.Should().Be(StatutoryDiscountOneShotResultClassifications.AwaitingReview);
        second.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceChannelPendingReviewCreated_RecordsSafeReviewLinkage()
    {
        var fixture = CreateFixture();
        var command = Command(sourceChannel: "WEBPAY", applyPayableBasis: false);

        var result = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        await fixture.ServiceChannelReviewRepository.Received(1).UpsertIntakeAsync(
            Arg.Is<StatutoryDiscountServiceChannelReviewIntakeCommand>(record =>
                record.StatutoryDiscountDecisionCommandId == result.StatutoryDiscountDecisionCommandId &&
                record.SourceChannel == "WEBPAY" &&
                record.MaskedIdReference == "SC-****-1234" &&
                record.EvidenceReferences.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAvailabilityAsync_ReturnsChannelSafeLocalOrdinanceFacts()
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.ResolveAvailabilityAsync(
            new StatutoryDiscountParkingAvailabilityRequest(
                RequestReference,
                ParkingSessionId,
                "SENIOR_CITIZEN",
                BeneficiaryResidencySatisfied: true,
                CorrelationId),
            CancellationToken.None);

        result.AvailabilityStatus.Should().Be(StatutoryDiscountParkingAvailabilityStatuses.Available);
        result.SiteId.Should().Be(SiteId);
        result.SiteGroupId.Should().Be(SiteGroupId);
        result.JurisdictionDisplayName.Should().Be("Paranaque City");
        result.VerificationStatus.Should().Be("VERIFIED_ACTIVE_OPERATIONAL");
        result.OrdinanceNumber.Should().BeNull();
        result.OrdinanceTextAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_WhenLocalOrdinanceUnavailable_FailsClosedBeforeDecisionCreation()
    {
        var unavailable = AvailablePolicy(new StatutoryDiscountParkingAvailabilityRequest(
            RequestReference,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            BeneficiaryResidencySatisfied: true,
            CorrelationId)) with
        {
            AvailabilityStatus = StatutoryDiscountParkingAvailabilityStatuses.NoApplicableLocalOrdinance,
            StatutoryParkingBenefitAvailable = false,
            PolicyVersionId = null,
            SafeReasonCode = "STATUTORY_DISCOUNT_LOCAL_ORDINANCE_UNAVAILABLE",
            RemediationAction = StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment
        };
        var fixture = CreateFixture(unavailable);

        var action = () => fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: false), CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_LOCAL_ORDINANCE_UNAVAILABLE");
        fixture.Repository.LastDecisionCommand.Should().BeNull();
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ServiceChannelReviewRepository.DidNotReceiveWithAnyArgs().UpsertIntakeAsync(default!, default);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyChangesRequestReference_ReplaysOriginalResult()
    {
        var fixture = CreateFixture();
        var command = Command();
        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var second = await fixture.Sut.SubmitAsync(
            command with { RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000202") },
            CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.RequestReference.Should().Be(RequestReference);
        second.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceChannelUsesDifferentKeyAndChannel_ReplaysPendingReviewResult()
    {
        var fixture = CreateFixture();
        var command = Command(sourceChannel: "WEBPAY", applyPayableBasis: false);
        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var second = await fixture.Sut.SubmitAsync(
            command with
            {
                SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
                RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000203"),
                IdempotencyKey = "statutory-discount-idempotency-key-apt"
            },
            CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.SourceChannel.Should().Be("WEBPAY");
        second.ResultClassification.Should().Be(StatutoryDiscountOneShotResultClassifications.AwaitingReview);
        second.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyHasDifferentMaterialFacts_ReturnsConflict()
    {
        var fixture = CreateFixture();
        var command = Command();
        await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var action = () => fixture.Sut.SubmitAsync(command with { EntitlementType = "PWD" }, CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT");
    }

    [Fact]
    public async Task SubmitAsync_WhenSameBusinessRequestChangesEvidenceFact_ReturnsConflict()
    {
        var fixture = CreateFixture();
        var command = Command();
        await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var action = () => fixture.Sut.SubmitAsync(
            command with
            {
                IdempotencyKey = "statutory-discount-idempotency-key-evidence-change",
                EvidenceReferences =
                [
                    command.EvidenceReferences[0] with { VerificationStatus = "REJECTED" }
                ]
            },
            CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT");
    }

    [Fact]
    public async Task SubmitAsync_WhenExistingProcessingUsesDifferentKey_ReturnsInProgressConflict()
    {
        var fixture = CreateFixture();
        var command = Command();
        fixture.Repository.SeedProcessing(command);

        var action = () => fixture.Sut.SubmitAsync(
            command with { IdempotencyKey = "statutory-discount-idempotency-key-webpay" },
            CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS");
        await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenExistingProcessingUsesOriginalKey_ReturnsRecoverableResultWithoutReexecutingWorkflow()
    {
        var fixture = CreateFixture();
        var command = Command();
        fixture.Repository.SeedProcessing(command);

        var result = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        result.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        result.DecisionStatus.Should().Be("APPLIED_PAYABLE_BASIS");
        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.Completed);
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.Applied);
        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenConcurrentWebPayRequestsArrive_CreatesOnePendingReviewDecision()
    {
        var fixture = CreateFixture();
        fixture.Repository.DelayInsideLock = TimeSpan.FromMilliseconds(25);
        var command = Command(sourceChannel: "WEBPAY", applyPayableBasis: false);
        var replay = command with
        {
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000204"),
            IdempotencyKey = "statutory-discount-idempotency-key-webpay"
        };

        var results = await Task.WhenAll(
            fixture.Sut.SubmitAsync(command, CancellationToken.None),
            fixture.Sut.SubmitAsync(replay, CancellationToken.None));

        results.Select(result => result.StatutoryDiscountDecisionCommandId).Distinct().Should().ContainSingle();
        results.Should().OnlyContain(result => result.DecisionCommandStatus == StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenConcurrentWebPayAndAptPendingReviewRequestsArrive_CreatesOneDecision()
    {
        var fixture = CreateFixture();
        fixture.Repository.DelayInsideLock = TimeSpan.FromMilliseconds(25);
        var webPayCommand = Command(sourceChannel: "WEBPAY", applyPayableBasis: false);
        var aptCommand = webPayCommand with
        {
            SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000205"),
            IdempotencyKey = "statutory-discount-idempotency-key-apt"
        };

        var results = await Task.WhenAll(
            fixture.Sut.SubmitAsync(webPayCommand, CancellationToken.None),
            fixture.Sut.SubmitAsync(aptCommand, CancellationToken.None));

        results.Select(result => result.StatutoryDiscountDecisionCommandId).Distinct().Should().ContainSingle();
        results.Should().OnlyContain(result => result.DecisionCommandStatus == StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenReplayed_DoesNotApplyPayableBasisAgain()
    {
        var fixture = CreateFixture();
        var command = Command();

        await fixture.Sut.SubmitAsync(command, CancellationToken.None);
        await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenApplyPayableBasisFalse_CompletesDecisionWithoutApplication()
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(applyPayableBasis: false), CancellationToken.None);

        result.DecisionStatus.Should().Be("APPROVED");
        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.Completed);
        result.ApplicationRequested.Should().BeFalse();
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().BeNull();
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenDecisionOnlyReplayLaterRequestsApplication_CreatesOnlyMissingApplicationStage()
    {
        var fixture = CreateFixture();
        var command = Command(applyPayableBasis: false);
        var decisionOnly = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var applied = await fixture.Sut.SubmitAsync(
            command with
            {
                ApplyPayableBasis = true,
                RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000208"),
                IdempotencyKey = "statutory-discount-idempotency-key-apply-later"
            },
            CancellationToken.None);

        applied.StatutoryDiscountDecisionCommandId.Should().Be(decisionOnly.StatutoryDiscountDecisionCommandId);
        applied.DecisionStatus.Should().Be("APPLIED_PAYABLE_BASIS");
        applied.ApplicationRequested.Should().BeTrue();
        applied.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.Applied);
        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenDecisionIsRejected_DoesNotCreateApplication()
    {
        var fixture = CreateFixture();
        fixture.DecisionService.DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>())
            .Returns(RejectedDecisionResult());

        var result = await fixture.Sut.SubmitAsync(
            Command(applyPayableBasis: false) with
            {
                Decision = "REJECT",
                DecisionReasonCode = "INELIGIBLE"
            },
            CancellationToken.None);

        result.DecisionStatus.Should().Be("REJECTED");
        result.ApplicationRequested.Should().BeFalse();
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenReferenceExists_ReturnsCanonicalReadback()
    {
        var fixture = CreateFixture();
        var submitted = await fixture.Sut.SubmitAsync(Command(), CancellationToken.None);

        var readback = await fixture.Sut.GetAsync(submitted.StatutoryDiscountDecisionCommandId, CorrelationId, CancellationToken.None);

        readback.Should().NotBeNull();
        readback!.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        readback.StatutoryDiscountValidationId.Should().Be(ValidationId);
        readback.SourceChannel.Should().Be("OPERATOR_CONSOLE");
        readback.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.Applied);
        readback.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
    }

    [Fact]
    public async Task SubmitAsync_WhenWebPaySubmitsPermittedFacts_CreatesAwaitingReviewDecisionOnly()
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: false), CancellationToken.None);

        result.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        result.DecisionStatus.Should().Be("AWAITING_REVIEW");
        result.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
        result.DecisionRetryable.Should().BeFalse();
        result.DecisionRecoveryClassification.Should().Be(StatutoryDiscountDecisionRecoveryClassifications.AwaitingReview);
        result.DecisionRecoveryAction.Should().Be(StatutoryDiscountDecisionRecoveryActions.WaitForReview);
        result.ApplicationRequested.Should().BeFalse();
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
        result.OneShotComplete.Should().BeFalse();
        fixture.Repository.ApplicationCount.Should().Be(0);
        fixture.Repository.LastDecisionCommand!.ActorUserId.Should().Be(Guid.Empty);
        await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.DecisionService.DidNotReceive().DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenAptSubmitsPermittedFacts_CreatesAwaitingReviewDecisionOnly()
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: "ASSISTED_PAYMENT_TERMINAL", applyPayableBasis: false), CancellationToken.None);

        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        result.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceChannelRequestsApplyPayableBasisWhileAwaitingReview_DoesNotBypassPendingReview()
    {
        var fixture = CreateFixture();
        await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: false), CancellationToken.None);

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: true), CancellationToken.None);

        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        result.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
        result.ApplicationRequested.Should().BeFalse();
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceChannelRequestsApplyPayableBasisWithoutDecision_ReturnsNotFound()
    {
        var fixture = CreateFixture();

        var action = () => fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: true), CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_DECISION_NOT_FOUND" && ex.IsNotFound);
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenApprovedWebPayDecisionRequestsApplicationIntent_AppliesPayableBasis()
    {
        var fixture = CreateFixture();
        await CreateApprovedServiceChannelDecisionAsync(fixture, "WEBPAY");

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: true), CancellationToken.None);

        result.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.Completed);
        result.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
        result.ApplicationRequested.Should().BeTrue();
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.Applied);
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().NotBeNull();
        result.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.NetPayableAmountMinorUnits.Should().Be(8929);
        await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.DecisionService.DidNotReceive().DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenApprovedAptDecisionRequestsApplicationIntent_AppliesPayableBasis()
    {
        var fixture = CreateFixture();
        await CreateApprovedServiceChannelDecisionAsync(fixture, "ASSISTED_PAYMENT_TERMINAL");

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: "ASSISTED_PAYMENT_TERMINAL", applyPayableBasis: true), CancellationToken.None);

        result.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
        result.ApplicationRequested.Should().BeTrue();
        result.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.Applied);
        fixture.Repository.ApplicationCount.Should().Be(1);
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenRejectedServiceChannelDecisionRequestsApplicationIntent_DoesNotCreateApplication()
    {
        var fixture = CreateFixture();
        await CreateRejectedServiceChannelDecisionAsync(fixture, "WEBPAY");

        var action = () => fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: true), CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED");
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenServiceChannelApplicationIntentCrossesChannels_ReplaysApplication()
    {
        var fixture = CreateFixture();
        await CreateApprovedServiceChannelDecisionAsync(fixture, "WEBPAY");
        var first = await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: true), CancellationToken.None);

        var replay = await fixture.Sut.SubmitAsync(
            Command(sourceChannel: "ASSISTED_PAYMENT_TERMINAL", applyPayableBasis: true) with
            {
                RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000209"),
                IdempotencyKey = "statutory-discount-idempotency-key-apt-apply"
            },
            CancellationToken.None);

        replay.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        replay.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(first.StatutoryDiscountPayableBasisApplicationCommandId);
        replay.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.Applied);
        fixture.Repository.ApplicationCount.Should().Be(1);
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenConcurrentWebPayAndAptApplicationIntentArrives_CreatesOneApplication()
    {
        var fixture = CreateFixture();
        fixture.Repository.DelayInsideLock = TimeSpan.FromMilliseconds(25);
        await CreateApprovedServiceChannelDecisionAsync(fixture, "WEBPAY");
        var webPay = Command(sourceChannel: "WEBPAY", applyPayableBasis: true);
        var apt = webPay with
        {
            SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000210"),
            IdempotencyKey = "statutory-discount-idempotency-key-apt-application"
        };

        var results = await Task.WhenAll(
            fixture.Sut.SubmitAsync(webPay, CancellationToken.None),
            fixture.Sut.SubmitAsync(apt, CancellationToken.None));

        results.Select(result => result.StatutoryDiscountPayableBasisApplicationCommandId).Distinct().Should().ContainSingle();
        var allowedStatuses = new[]
        {
            StatutoryDiscountApplicationStageStatuses.Applied,
            StatutoryDiscountPayableBasisApplicationV1CommandStates.Received,
            StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing
        };
        results.Should().OnlyContain(result => allowedStatuses.Contains(result.ApplicationCommandStatus));
        fixture.Repository.ApplicationCount.Should().Be(1);
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenApprovedDecisionLacksPayableBasisFacts_DoesNotApply()
    {
        var fixture = CreateFixture();
        var pending = await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: false), CancellationToken.None);
        await fixture.Repository.CompleteDecisionApprovedAsync(
            pending.StatutoryDiscountDecisionCommandId,
            ValidationId,
            OriginalTariffSnapshotId,
            PolicyId,
            fallbackPolicyReferenceId: null,
            "NATIONAL_LAW_FALLBACK",
            localOrdinanceApplied: false,
            tariffFacts: null,
            "ELIGIBLE",
            CorrelationId,
            CancellationToken.None);

        var action = () => fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: true), CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_PAYABLE_BASIS_FACTS_UNAVAILABLE");
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenApprovedDecisionLacksFrozenPolicyAuthority_DoesNotApply()
    {
        var fixture = CreateFixture();
        await CreateApprovedServiceChannelDecisionAsync(fixture, "WEBPAY");
        fixture.ParkingEligibilityRepository.Clear(CommandId);
        fixture.ParkingEligibilityRepository.ReturnDefaultAuthority = false;

        var action = () => fixture.Sut.SubmitAsync(
            Command(sourceChannel: "WEBPAY", applyPayableBasis: true) with
            {
                RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000210"),
                IdempotencyKey = "statutory-discount-idempotency-key-apply-policy-required"
            },
            CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_POLICY_AUTHORITY_REQUIRED");
        fixture.Repository.ApplicationCount.Should().Be(0);
        await fixture.ApplyService.DidNotReceive().ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenPendingReviewReferenceExists_ReturnsAwaitingReviewReadback()
    {
        var fixture = CreateFixture();
        var submitted = await fixture.Sut.SubmitAsync(Command(sourceChannel: "WEBPAY", applyPayableBasis: false), CancellationToken.None);

        var readback = await fixture.Sut.GetAsync(submitted.StatutoryDiscountDecisionCommandId, CorrelationId, CancellationToken.None);

        readback.Should().NotBeNull();
        readback!.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionCommandStatuses.AwaitingReview);
        readback.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
        readback.ApplicationRequested.Should().BeFalse();
        readback.ApplicationCommandStatus.Should().Be(StatutoryDiscountApplicationStageStatuses.NotRequested);
        readback.OneShotComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenReferenceMissing_ReturnsNull()
    {
        var fixture = CreateFixture();

        var readback = await fixture.Sut.GetAsync(Guid.Parse("6d000000-0000-0000-0000-000000000099"), CorrelationId, CancellationToken.None);

        readback.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_WhenFullStatutoryIdIsProvided_RejectsUnsafeIdentifier()
    {
        var fixture = CreateFixture();

        var action = () => fixture.Sut.SubmitAsync(Command(maskedIdReference: "SC-123456789"), CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "UNSAFE_IDENTIFIER_REJECTED");
    }

    [Fact]
    public void SemanticHash_ExcludesCorrelationId()
    {
        var first = Command(correlationId: Guid.Parse("6d000000-0000-0000-0000-000000000101"));
        var second = first with { CorrelationId = Guid.Parse("6d000000-0000-0000-0000-000000000102") };

        StatutoryDiscountDecisionSemanticHash.Compute(first)
            .Should().Be(StatutoryDiscountDecisionSemanticHash.Compute(second));
    }

    [Fact]
    public void SemanticHash_ExcludesSourceChannelAndRequestReference()
    {
        var first = Command();
        var second = first with
        {
            SourceChannel = "WEBPAY",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000206")
        };

        StatutoryDiscountDecisionSemanticHash.Compute(first)
            .Should().Be(StatutoryDiscountDecisionSemanticHash.Compute(second));
    }

    [Fact]
    public void IdempotencyScope_UsesParkingSessionAndEntitlementOnly()
    {
        var first = Command();
        var second = first with
        {
            SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000207")
        };

        StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(first)
            .Should().Be("statutory-discount-decision:6d000000000000000000000000000003:SENIOR_CITIZEN");
        StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(second)
            .Should().Be(StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(first));
    }

    [Fact]
    public void SemanticHash_IncludesMaterialFacts()
    {
        var first = Command();
        var second = first with { EntitlementType = "PWD" };

        StatutoryDiscountDecisionSemanticHash.Compute(first)
            .Should().NotBe(StatutoryDiscountDecisionSemanticHash.Compute(second));
    }

    private static async Task CreateApprovedServiceChannelDecisionAsync(TestFixture fixture, string sourceChannel)
    {
        var pending = await fixture.Sut.SubmitAsync(Command(sourceChannel: sourceChannel, applyPayableBasis: false), CancellationToken.None);
        await fixture.Repository.CompleteDecisionApprovedAsync(
            pending.StatutoryDiscountDecisionCommandId,
            ValidationId,
            OriginalTariffSnapshotId,
            PolicyId,
            fallbackPolicyReferenceId: null,
            "NATIONAL_LAW_FALLBACK",
            localOrdinanceApplied: false,
            new StatutoryDiscountDecisionV2TariffFacts(12500, 11161, 1339, 2232, 8929, "PHP"),
            "ELIGIBLE",
            CorrelationId,
            CancellationToken.None);
    }

    private static async Task CreateRejectedServiceChannelDecisionAsync(TestFixture fixture, string sourceChannel)
    {
        var pending = await fixture.Sut.SubmitAsync(Command(sourceChannel: sourceChannel, applyPayableBasis: false), CancellationToken.None);
        await fixture.Repository.CompleteDecisionRejectedAsync(
            pending.StatutoryDiscountDecisionCommandId,
            "INELIGIBLE",
            "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED",
            CorrelationId,
            CancellationToken.None);
    }

    private static TestFixture CreateFixture(StatutoryDiscountParkingAvailabilityResult? availability = null)
    {
        var repository = new InMemoryStagedCommandService();
        var historicalRepository = Substitute.For<IStatutoryDiscountDecisionFacadeRepository>();
        var draftService = Substitute.For<IOperatorConsoleStatutoryDiscountDraftService>();
        draftService.DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>())
            .Returns(DraftResult());

        var evidenceService = Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceService>();
        evidenceService.CaptureAsync(Arg.Any<OperatorConsoleStatutoryDiscountEvidenceCaptureCommand>(), Arg.Any<CancellationToken>())
            .Returns(EvidenceResult());

        var decisionService = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionService>();
        decisionService.DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>())
            .Returns(DecisionResult());

        var applyService = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisService>();
        applyService.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>())
            .Returns(ApplyResult());

        var readService = Substitute.For<IOperatorConsoleStatutoryDiscountReadService>();
        readService.GetDraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(DetailResult());

        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);
        var serviceChannelReviewRepository = Substitute.For<IStatutoryDiscountServiceChannelReviewRepository>();
        serviceChannelReviewRepository.GetValidationReviewerUserIdAsync(ValidationId, Arg.Any<CancellationToken>())
            .Returns(ReviewerUserId);
        var parkingEligibilityRepository = new InMemoryParkingEligibilityRepository();
        var parkingEligibilityResolver = Substitute.For<IStatutoryDiscountParkingEligibilityResolver>();
        parkingEligibilityResolver.ResolveAsync(Arg.Any<StatutoryDiscountParkingAvailabilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => availability ?? AvailablePolicy((StatutoryDiscountParkingAvailabilityRequest)call[0]!));

        var sut = new StatutoryDiscountDecisionFacadeService(
            repository,
            historicalRepository,
            draftService,
            evidenceService,
            decisionService,
            applyService,
            readService,
            serviceChannelReviewRepository,
            parkingEligibilityResolver,
            parkingEligibilityRepository);

        return new TestFixture(
            repository,
            draftService,
            evidenceService,
            decisionService,
            applyService,
            serviceChannelReviewRepository,
            parkingEligibilityRepository,
            sut);
    }

    private static StatutoryDiscountDecisionCommand Command(
        string sourceChannel = "OPERATOR_CONSOLE",
        string entitlementType = "SENIOR_CITIZEN",
        bool applyPayableBasis = true,
        string maskedIdReference = "SC-****-1234",
        Guid? correlationId = null)
    {
        var operatorConsole = string.Equals(sourceChannel, "OPERATOR_CONSOLE", StringComparison.Ordinal);

        return new(
            RequestReference,
            sourceChannel,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            "TICKET-001",
            "ABC1234",
            entitlementType,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            maskedIdReference,
            true,
            [new StatutoryDiscountEvidenceReference(
                "SENIOR_CITIZEN_ID",
                "MANUAL_REFERENCE",
                null,
                null,
                null,
                "evidence-ref-001",
                "SC-****-1234",
                "VERIFIED")],
            ActorUserId,
            operatorConsole ? DeviceBindingId : null,
            operatorConsole ? ShiftId : null,
            true,
            "attested",
            "CUSTOMER_REQUEST",
            operatorConsole ? "APPROVE" : null,
            operatorConsole ? "ELIGIBLE" : null,
            operatorConsole ? ReviewerUserId : null,
            operatorConsole,
            applyPayableBasis,
            OriginalTariffSnapshotId,
            BeneficiaryResidencySatisfied: true,
            "statutory-discount-idempotency-key",
            correlationId ?? CorrelationId);
    }

    private static StatutoryDiscountParkingAvailabilityResult AvailablePolicy(
        StatutoryDiscountParkingAvailabilityRequest request) =>
        new(
            request.RequestReference,
            request.ParkingSessionId,
            SiteId,
            SiteGroupId,
            Guid.Parse("6d000000-0000-0000-0000-000000000070"),
            "137604000",
            "Paranaque City",
            StatutoryDiscountParkingAvailabilityStatuses.Available,
            StatutoryParkingBenefitAvailable: true,
            [request.RequestedEntitlementType ?? "SENIOR_CITIZEN"],
            request.RequestedEntitlementType,
            Guid.Parse("6d000000-0000-0000-0000-000000000071"),
            PolicyId,
            "PARANAQUE-SC-PWD-FREE-PARKING",
            "v1",
            OrdinanceNumber: null,
            OrdinanceTitle: null,
            "Paranaque resident statutory parking benefit",
            "VERIFIED_ACTIVE_OPERATIONAL",
            "ACTIVE_FOR_TRANSACTION_USE",
            "DETAILS_PARTIALLY_VERIFIED",
            EffectiveFrom: null,
            EffectiveTo: null,
            "RESIDENT_ONLY",
            [new StatutoryDiscountPolicyEvidenceRequirement(
                "RESIDENCY_EVIDENCE",
                "REQUIRED",
                "Residency evidence",
                SafeRequirementNotes: null)],
            "COVERED",
            "PERCENTAGE_DISCOUNT",
            "SUPPORTED_BY_CURRENT_CALCULATION",
            OfficialSourceAvailable: false,
            OrdinanceTextAvailable: false,
            OrdinanceNumberAvailable: false,
            "PARANAQUE_OPERATIONAL_AUTHORITY",
            "controlled-policy-record",
            SafeReasonCode: null,
            Retryable: false,
            StatutoryDiscountParkingAvailabilityRemediationActions.ContinueWithOrdinaryPayment,
            Now,
            "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            request.CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult DraftResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000020"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            ValidationId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            true,
            true,
            true,
            Guid.Parse("6d000000-0000-0000-0000-000000000021"),
            false,
            null,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureResult EvidenceResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000030"),
            ValidationId,
            "SENIOR_CITIZEN_ID",
            "MANUAL_REFERENCE",
            null,
            null,
            null,
            "evidence-ref-001",
            "SC-****-1234",
            ActorUserId,
            Now,
            "NOT_REDACTED",
            "PENDING_REVIEW",
            true,
            "REQUESTED",
            true,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionResult DecisionResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000040"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            ValidationId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            "APPROVED",
            "APPROVE",
            "ELIGIBLE",
            false,
            true,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionResult RejectedDecisionResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000041"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            ValidationId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            "REJECTED",
            "REJECT",
            "INELIGIBLE",
            false,
            true,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisResult ApplyResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000050"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            PayableBasisApplicationId,
            ValidationId,
            ParkingSessionId,
            OriginalTariffSnapshotId,
            AppliedTariffSnapshotId,
            "APPLIED",
            false,
            12500,
            1339,
            11161,
            2232,
            8929,
            "PHP",
            PolicyId,
            null,
            "NATIONAL_LAW_FALLBACK",
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            "RA 9994",
            null,
            true,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftDetailResult DetailResult() =>
        new(
            ValidationId,
            ParkingSessionId,
            "TICKET-001",
            "ABC1234",
            SiteId,
            "Site",
            SiteGroupId,
            "SENIOR_CITIZEN",
            "APPROVED",
            StatutoryDiscountDecisionCommandId: null,
            IdDocumentType: null,
            IssuingAuthority: null,
            ExpiryDate: null,
            MaskedIdReference: null,
            RequesterAttestation: null,
            AttestationNotes: null,
            true,
            true,
            true,
            1,
            "PENDING_REVIEW",
            ["SENIOR_CITIZEN_ID"],
            Now,
            Now.AddMinutes(1),
            ActorUserId,
            ReviewerUserId,
            "ELIGIBLE",
            null,
            "NATIONAL_LAW_FALLBACK",
            PolicyId,
            null,
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "Senior Citizen National Fallback",
            "RA 9994",
            null,
            "RA 9994",
            "ACTIVE",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            null,
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "VAT_EXCLUSIVE",
            "STATUTORY_FIRST",
            null,
            OriginalTariffSnapshotId,
            PayableBasisApplicationId,
            null,
            "APPLIED",
            AppliedTariffSnapshotId,
            12500,
            1339,
            11161,
            2232,
            8929,
            8929,
            "PHP",
            []);

    private sealed record TestFixture(
        InMemoryStagedCommandService Repository,
        IOperatorConsoleStatutoryDiscountDraftService DraftService,
        IOperatorConsoleStatutoryDiscountEvidenceService EvidenceService,
        IOperatorConsoleStatutoryDiscountDecisionService DecisionService,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisService ApplyService,
        IStatutoryDiscountServiceChannelReviewRepository ServiceChannelReviewRepository,
        InMemoryParkingEligibilityRepository ParkingEligibilityRepository,
        StatutoryDiscountDecisionFacadeService Sut);

    private sealed class InMemoryParkingEligibilityRepository : IStatutoryDiscountParkingEligibilityRepository
    {
        private readonly Dictionary<Guid, StatutoryDiscountDecisionPolicyAuthority> _authorities = [];

        public bool ReturnDefaultAuthority { get; set; } = true;

        public Task<StatutoryDiscountParkingAvailabilityResult> ResolveAsync(
            StatutoryDiscountParkingAvailabilityRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(AvailablePolicy(request));

        public Task BindDecisionPolicyAuthorityAsync(
            Guid statutoryDiscountDecisionCommandId,
            StatutoryDiscountParkingAvailabilityResult availability,
            CancellationToken cancellationToken)
        {
            _authorities[statutoryDiscountDecisionCommandId] = new StatutoryDiscountDecisionPolicyAuthority(
                statutoryDiscountDecisionCommandId,
                availability.PolicyVersionId!.Value,
                availability.JurisdictionId!.Value,
                availability.JurisdictionCode!,
                availability.JurisdictionDisplayName!,
                availability.PolicyCode!,
                availability.PolicyVersion!,
                availability.RequestedEntitlementType!,
                availability.VerificationStatus!,
                availability.PublicationStatus!,
                availability.DetailedRuleVerificationStatus!,
                availability.ParkingServiceApplicability!,
                availability.BenefitEffectClassification!,
                availability.ResidencyRequirement!,
                availability.OfficialSourceAvailable,
                availability.OrdinanceTextAvailable,
                availability.OrdinanceNumberAvailable,
                availability.OrdinanceNumber,
                availability.OrdinanceTitle,
                availability.LegalBasisReference,
                availability.SourceReference!,
                availability.EffectiveFrom,
                availability.EffectiveTo,
                availability.TransactionAt ?? Now,
                availability.PolicySemanticHash!,
                availability.CorrelationId);
            return Task.CompletedTask;
        }

        public Task<StatutoryDiscountDecisionPolicyAuthority?> GetDecisionPolicyAuthorityAsync(
            Guid statutoryDiscountDecisionCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_authorities.TryGetValue(statutoryDiscountDecisionCommandId, out var authority)
                ? authority
                : ReturnDefaultAuthority && statutoryDiscountDecisionCommandId == CommandId
                    ? DefaultAuthority(statutoryDiscountDecisionCommandId)
                    : null);

        public void Clear(Guid statutoryDiscountDecisionCommandId) => _authorities.Remove(statutoryDiscountDecisionCommandId);

        private static StatutoryDiscountDecisionPolicyAuthority DefaultAuthority(Guid statutoryDiscountDecisionCommandId) =>
            new(
                statutoryDiscountDecisionCommandId,
                PolicyId,
                Guid.Parse("6d000000-0000-0000-0000-000000000070"),
                "137604000",
                "Paranaque City",
                "PARANAQUE-SC-PWD-FREE-PARKING",
                "v1",
                "SENIOR_CITIZEN",
                "VERIFIED_ACTIVE_OPERATIONAL",
                "ACTIVE_FOR_TRANSACTION_USE",
                "DETAILS_PARTIALLY_VERIFIED",
                "COVERED",
                "PERCENTAGE_DISCOUNT",
                "RESIDENT_ONLY",
                OfficialSourceAvailable: false,
                OrdinanceTextAvailable: false,
                OrdinanceNumberAvailable: false,
                OrdinanceNumber: null,
                OrdinanceTitle: null,
                LegalBasisReference: "PARANAQUE_OPERATIONAL_AUTHORITY",
                SourceReference: "controlled-policy-record",
                TransactionUseEffectiveFrom: null,
                TransactionUseEffectiveTo: null,
                ResolvedAt: Now,
                "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                CorrelationId);
    }

    private sealed class InMemoryStagedCommandService : IStatutoryDiscountStagedCommandService
    {
        private StatutoryDiscountDecisionV2Record? _decision;
        private StatutoryDiscountPayableBasisApplicationV1Record? _application;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public StatutoryDiscountDecisionV2Command? LastDecisionCommand { get; private set; }

        public int ApplicationCount => _application is null ? 0 : 1;

        public TimeSpan DelayInsideLock { get; set; }

        public async Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>> CreateOrResolveDecisionAsync(
            StatutoryDiscountDecisionV2Command command,
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                LastDecisionCommand = command;
                if (DelayInsideLock > TimeSpan.Zero)
                {
                    await Task.Delay(DelayInsideLock, cancellationToken);
                }

                var hash = StatutoryDiscountDecisionV2SemanticHash.Compute(command);
                var businessIdentity = StatutoryDiscountDecisionV2SemanticHash.BuildBusinessIdentity(command);
                if (_decision is not null)
                {
                    var conflict = _decision.SemanticHashSourceVersion != StatutoryDiscountDecisionV2SemanticHash.SourceVersion ||
                        _decision.SemanticRequestHash != hash;
                    var recoverable = _decision.IdempotencyKey == command.IdempotencyKey &&
                        _decision.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Received
                            or StatutoryDiscountDecisionV2CommandStates.Processing;
                    return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                        conflict
                            ? StatutoryDiscountDecisionClientResultStatuses.SemanticConflict
                            : recoverable
                                ? StatutoryDiscountDecisionClientResultStatuses.RecoverableUsingOriginalKey
                                : StatutoryDiscountDecisionClientResultStatuses.IdempotentReplay,
                        Existing: true,
                        SemanticConflict: conflict,
                        Retryable: recoverable,
                        recoverable
                            ? StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey
                            : StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                        _decision,
                        conflict ? "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT" : null);
                }

                _decision = CreateDecisionRecord(command, businessIdentity, hash);
                return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                    StatutoryDiscountDecisionClientResultStatuses.CreatedDurablyCompleted,
                    Existing: false,
                    SemanticConflict: false,
                    Retryable: false,
                    StatutoryDiscountDecisionRecoveryClassifications.None,
                    _decision,
                    SafeErrorCode: null);
            }
            finally
            {
                _lock.Release();
            }
        }

        public void SeedProcessing(StatutoryDiscountDecisionCommand command)
        {
            var decisionCommand = ToDecisionV2(command, DeriveTestStageKey(command.IdempotencyKey)) with
            {
                PolicyResolutionReferenceId = PolicyId,
                AppliedPolicyReferenceId = PolicyId,
                PolicyResolutionBasis = "LOCAL_ORDINANCE_APPLIED",
                LocalOrdinanceApplied = true
            };
            _decision = CreateDecisionRecord(
                decisionCommand,
                StatutoryDiscountDecisionV2SemanticHash.BuildBusinessIdentity(decisionCommand),
                StatutoryDiscountDecisionV2SemanticHash.Compute(decisionCommand)) with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Processing,
                ProcessingStartedAt = Now
            };
        }

        public Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_decision?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId
                ? _decision
                : null);

        public Task<StatutoryDiscountDecisionV2Record?> GetDecisionByBusinessIdentityAsync(
            string businessIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(_decision?.BusinessIdentity, businessIdentity, StringComparison.Ordinal)
                || string.Equals(_decision?.IdempotencyScope, businessIdentity, StringComparison.Ordinal)
                    ? _decision
                    : null);

        public Task<StatutoryDiscountDecisionV2Record> MarkDecisionProcessingAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = _decision! with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Processing,
                Retryable = true,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey,
                CorrelationId = correlationId,
                ProcessingStartedAt = _decision.ProcessingStartedAt ?? Now
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> MarkDecisionAwaitingReviewAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = _decision! with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.AwaitingReview,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.NotDecided,
                ResultClassification = StatutoryDiscountOneShotResultClassifications.AwaitingReview,
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.AwaitingReview,
                CorrelationId = correlationId
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> CompleteDecisionApprovedAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid? statutoryDiscountValidationId,
            Guid? originalTariffSnapshotId,
            Guid? appliedPolicyReferenceId,
            Guid? fallbackPolicyReferenceId,
            string? policyResolutionBasis,
            bool localOrdinanceApplied,
            StatutoryDiscountDecisionV2TariffFacts? tariffFacts,
            string? reasonCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = _decision! with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Approved,
                ResultClassification = StatutoryDiscountDecisionClientResultStatuses.Approved,
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                StatutoryDiscountValidationId = statutoryDiscountValidationId,
                OriginalTariffSnapshotId = originalTariffSnapshotId,
                AppliedPolicyReferenceId = appliedPolicyReferenceId,
                PolicyResolutionBasis = policyResolutionBasis,
                LocalOrdinanceApplied = localOrdinanceApplied,
                GrossAmountMinorUnits = tariffFacts?.GrossAmountMinorUnits,
                VatExclusiveAmountMinorUnits = tariffFacts?.VatExclusiveAmountMinorUnits,
                VatAmountMinorUnits = tariffFacts?.VatAmountMinorUnits,
                StatutoryDiscountAmountMinorUnits = tariffFacts?.StatutoryDiscountAmountMinorUnits,
                NetPayableAmountMinorUnits = tariffFacts?.NetPayableAmountMinorUnits,
                Currency = tariffFacts?.Currency,
                ReasonCode = reasonCode,
                CorrelationId = correlationId,
                DecidedAt = Now,
                CompletedAt = Now
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> CompleteDecisionRejectedAsync(
            Guid statutoryDiscountDecisionCommandId,
            string? reasonCode,
            string? safeErrorCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = _decision! with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Rejected,
                ResultClassification = StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved,
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                SafeErrorCode = safeErrorCode,
                ReasonCode = reasonCode,
                CorrelationId = correlationId,
                DecidedAt = Now,
                CompletedAt = Now
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> RecordDecisionFailureAsync(
            Guid statutoryDiscountDecisionCommandId,
            bool retryable,
            string safeErrorCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = _decision! with
            {
                CommandStatus = retryable
                    ? StatutoryDiscountDecisionV2CommandStates.FailedRetryable
                    : StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable,
                Retryable = retryable,
                SafeErrorCode = safeErrorCode,
                CorrelationId = correlationId,
                FailedAt = Now
            };
            return Task.FromResult(_decision);
        }

        public async Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>> CreateOrResolveApplicationAsync(
            StatutoryDiscountPayableBasisApplicationV1Command command,
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var hash = StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(command);
                if (_application is not null)
                {
                    var conflict = _application.SemanticRequestHash != hash;
                    var recoverable = _application.IdempotencyKey == command.IdempotencyKey &&
                        _application.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Received
                            or StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing;
                    return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                        conflict
                            ? StatutoryDiscountPayableBasisApplicationV1ResultClassifications.SemanticConflict
                            : recoverable
                                ? StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress
                                : StatutoryDiscountPayableBasisApplicationV1ResultClassifications.IdempotentReplay,
                        Existing: true,
                        SemanticConflict: conflict,
                        Retryable: recoverable,
                        recoverable
                            ? StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey
                            : StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                        _application,
                        conflict ? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT" : null);
                }

                _application = CreateApplicationRecord(command, hash);
                return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                    StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
                    Existing: false,
                    SemanticConflict: false,
                    Retryable: false,
                    StatutoryDiscountDecisionRecoveryClassifications.None,
                    _application,
                    SafeErrorCode: null);
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_application?.StatutoryDiscountPayableBasisApplicationCommandId == statutoryDiscountPayableBasisApplicationCommandId
                ? _application
                : null);

        public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationByDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_application?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId
                ? _application
                : null);

        public Task<T> ExecuteWithApplicationLockAsync<T>(
            StatutoryDiscountPayableBasisApplicationV1Record application,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> MarkApplicationProcessingAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _application = _application! with
            {
                CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing,
                ResultClassification = StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
                Retryable = true,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey,
                CorrelationId = correlationId,
                ProcessingStartedAt = _application.ProcessingStartedAt ?? Now
            };
            return Task.FromResult(_application);
        }

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> CompleteApplicationAppliedAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            Guid? statutoryDiscountPayableBasisApplicationId,
            Guid? appliedTariffSnapshotId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _application = _application! with
            {
                CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied,
                ResultClassification = StatutoryDiscountPayableBasisApplicationV1ResultClassifications.Applied,
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                StatutoryDiscountPayableBasisApplicationId = statutoryDiscountPayableBasisApplicationId,
                AppliedTariffSnapshotId = appliedTariffSnapshotId,
                CorrelationId = correlationId,
                AppliedAt = Now,
                CompletedAt = Now
            };
            return Task.FromResult(_application);
        }

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> RecordApplicationFailureAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            bool retryable,
            string safeErrorCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _application = _application! with
            {
                CommandStatus = retryable
                    ? StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedRetryable
                    : StatutoryDiscountPayableBasisApplicationV1CommandStates.FailedNonRetryable,
                ResultClassification = retryable
                    ? StatutoryDiscountPayableBasisApplicationV1ResultClassifications.RetryableFailure
                    : StatutoryDiscountPayableBasisApplicationV1ResultClassifications.NonRetryableFailure,
                Retryable = retryable,
                SafeErrorCode = safeErrorCode,
                CorrelationId = correlationId,
                FailedAt = Now
            };
            return Task.FromResult(_application);
        }

        private static StatutoryDiscountDecisionV2Record CreateDecisionRecord(
            StatutoryDiscountDecisionV2Command command,
            string businessIdentity,
            string semanticHash) =>
            new(
                CommandId,
                command.RequestReference,
                command.ParkingSessionId,
                command.SourceChannel,
                command.EntitlementType.Trim().ToUpperInvariant(),
                businessIdentity,
                businessIdentity,
                command.IdempotencyKey,
                StatutoryDiscountDecisionV2SemanticHash.SourceVersion,
                semanticHash,
                StatutoryDiscountDecisionV2CommandStates.Received,
                StatutoryDiscountDecisionV2ResultStates.NotDecided,
                "ACCEPTED",
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.None,
                SafeErrorCode: null,
                StatutoryDiscountValidationId: null,
                command.OriginalTariffSnapshotId,
                AppliedPolicyReferenceId: null,
                FallbackPolicyReferenceId: null,
                PolicyResolutionBasis: null,
                LocalOrdinanceApplied: false,
                GrossAmountMinorUnits: null,
                VatExclusiveAmountMinorUnits: null,
                VatAmountMinorUnits: null,
                StatutoryDiscountAmountMinorUnits: null,
                NetPayableAmountMinorUnits: null,
                Currency: null,
                EvidenceRequired: command.EvidenceReferences.Count > 0,
                EvidenceRecorded: false,
                ReasonCode: null,
                command.CorrelationId,
                Now,
                ProcessingStartedAt: null,
                DecidedAt: null,
                CompletedAt: null,
                FailedAt: null,
                Now);

        private static StatutoryDiscountPayableBasisApplicationV1Record CreateApplicationRecord(
            StatutoryDiscountPayableBasisApplicationV1Command command,
            string semanticHash) =>
            new(
                Guid.Parse("6d000000-0000-0000-0000-000000000060"),
                command.RequestReference,
                command.StatutoryDiscountDecisionCommandId,
                command.ParkingSessionId,
                command.EntitlementType,
                StatutoryDiscountPayableBasisApplicationV1SemanticHash.BuildBusinessIdentity(command),
                StatutoryDiscountPayableBasisApplicationV1SemanticHash.BuildIdempotencyScope(command),
                command.IdempotencyKey,
                StatutoryDiscountPayableBasisApplicationV1SemanticHash.SourceVersion,
                semanticHash,
                StatutoryDiscountPayableBasisApplicationV1CommandStates.Received,
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.None,
                SafeErrorCode: null,
                command.StatutoryDiscountValidationId,
                StatutoryDiscountPayableBasisApplicationId: null,
                command.OriginalTariffSnapshotId,
                command.TargetTariffSnapshotId,
                command.AppliedTariffSnapshotId,
                command.AppliedPolicyReferenceId,
                command.PolicyResolutionBasis,
                command.ApprovedDiscountAmountMinorUnits,
                command.ApprovedVatExclusiveAmountMinorUnits,
                command.ApprovedVatAmountMinorUnits,
                command.ApprovedFinalPayableAmountMinorUnits,
                command.Currency,
                command.SourceChannel,
                command.CorrelationId,
                Now,
                ProcessingStartedAt: null,
                AppliedAt: null,
                CompletedAt: null,
                FailedAt: null,
                Now);

        private static StatutoryDiscountDecisionV2Command ToDecisionV2(
            StatutoryDiscountDecisionCommand command,
            string stageIdempotencyKey) =>
            new(
                command.RequestReference,
                command.SourceChannel,
                command.ParkingSessionId,
                command.SiteId,
                command.SiteGroupId,
                command.TicketReference,
                command.PlateNumber,
                command.EntitlementType,
                new StatutoryDiscountDecisionV2BeneficiaryMetadata(null, command.EntitlementType, null, 1),
                new StatutoryDiscountDecisionV2IdentityMetadata(
                    command.IdDocumentType,
                    command.IssuingAuthority,
                    command.ExpiryDate,
                    command.MaskedIdReference,
                    null),
                command.EvidenceReferences.Select(evidence => new StatutoryDiscountDecisionV2EvidenceReference(
                        evidence.EvidenceType,
                        evidence.CaptureMethod,
                        evidence.StorageReference,
                        evidence.ReferenceNumberMasked,
                        evidence.VerificationStatus,
                        null,
                        null))
                    .ToArray(),
                new StatutoryDiscountDecisionV2AttestationFacts(
                    command.RequesterAttestation,
                    null,
                    command.ReasonCode,
                    command.ReviewerAttestation),
                command.ActorUserId,
                command.ReviewerUserId,
                command.OperatorDeviceBindingId,
                command.OperatorShiftId,
                new StatutoryDiscountDecisionV2DecisionFacts(
                    command.Decision ?? StatutoryDiscountDecisionV2ResultStates.NotDecided,
                    command.DecisionReasonCode,
                    null),
                PolicyResolutionReferenceId: null,
                AppliedPolicyReferenceId: null,
                FallbackPolicyReferenceId: null,
                PolicyResolutionBasis: null,
                LocalOrdinanceApplied: false,
                command.OriginalTariffSnapshotId,
                OriginalTariffFacts: null,
                stageIdempotencyKey,
                command.CorrelationId);

        private static string DeriveTestStageKey(string idempotencyKey)
        {
            var source = $"statutory-discount-one-shot:decision-v2:{ParkingSessionId:N}:{idempotencyKey.Trim()}";
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
            return $"decision-v2:sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
        }
    }
}
