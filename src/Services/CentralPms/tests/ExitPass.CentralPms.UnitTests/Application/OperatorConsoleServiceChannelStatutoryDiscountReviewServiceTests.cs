using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleServiceChannelStatutoryDiscountReviewServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("8a000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("8a000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("8a000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("8a000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("8a000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("8a000000-0000-0000-0000-000000000006");
    private static readonly Guid CommandId = Guid.Parse("8a000000-0000-0000-0000-000000000007");
    private static readonly Guid RequestReference = Guid.Parse("8a000000-0000-0000-0000-000000000008");
    private static readonly Guid ParkingSessionId = Guid.Parse("8a000000-0000-0000-0000-000000000009");
    private static readonly Guid CorrelationId = Guid.Parse("8a000000-0000-0000-0000-00000000000a");
    private static readonly Guid TariffSnapshotId = Guid.Parse("8a000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-24T03:00:00Z");

    [Fact]
    public async Task ListAsync_WhenAuthorized_ReturnsPendingServiceChannelReviews()
    {
        var fixture = CreateFixture();
        fixture.Repository.ListAsync(Arg.Any<StatutoryDiscountServiceChannelReviewQueueQuery>(), Arg.Any<CancellationToken>())
            .Returns(new StatutoryDiscountServiceChannelReviewQueueResult(
                [QueueItem()],
                1,
                25,
                HasMore: false,
                CorrelationId));

        var result = await fixture.Sut.ListAsync(
            new StatutoryDiscountServiceChannelReviewQueueQuery(null, null, null, null, null, null, null, 1, 25, CorrelationId),
            AccessContext(),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        await fixture.AccessService.Received(1).EvaluateAsync(
            Arg.Is<OperatorConsoleAccessEvaluationCommand>(command =>
                command.ControlledActionCode == OperatorConsoleActionCodes.ViewStatutoryDiscountDraft),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenAuthorized_ReturnsSafeReviewDetail()
    {
        var fixture = CreateFixture();
        fixture.Repository.GetAsync(CommandId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ReviewDetail());

        var result = await fixture.Sut.GetAsync(CommandId, AccessContext(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.MaskedIdReference.Should().Be("SC-****-1234");
        result.EvidenceReferences.Should().ContainSingle();
        result.EvidenceReferences[0].ReferenceNumberMasked.Should().Be("SC-****-1234");
    }

    [Fact]
    public async Task DecideAsync_WhenApprovalAllowed_CompletesSameCanonicalDecision()
    {
        var fixture = CreateFixture();
        fixture.Repository.GetAsync(CommandId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ReviewDetail());
        fixture.Staged.GetDecisionAsync(CommandId, Arg.Any<CancellationToken>())
            .Returns(AwaitingDecision());
        fixture.Repository.EnsureApprovedValidationLinkageAsync(
                CommandId,
                UserId,
                "ELIGIBLE",
                CorrelationId,
                Arg.Any<CancellationToken>())
            .Returns(ValidationLinkage());
        fixture.Staged.CompleteDecisionApprovedAsync(
                CommandId,
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<StatutoryDiscountDecisionV2TariffFacts?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(CompletedDecision(StatutoryDiscountDecisionV2ResultStates.Approved));
        fixture.Repository.RecordReviewCompletionAsync(
                CommandId,
                UserId,
                DeviceBindingId,
                ShiftId,
                EvaluationId,
                "APPROVE",
                "ELIGIBLE",
                CorrelationId,
                Arg.Any<CancellationToken>())
            .Returns(ReviewDetail(reviewStatus: StatutoryDiscountServiceChannelReviewStatuses.Approved));

        var result = await fixture.Sut.DecideAsync(DecisionCommand(), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.CurrentCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Completed);
        result.CurrentDecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
        await fixture.Staged.Received(1).CompleteDecisionApprovedAsync(
            CommandId,
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<StatutoryDiscountDecisionV2TariffFacts?>(),
            Arg.Any<string?>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await fixture.Staged.DidNotReceiveWithAnyArgs().CreateOrResolveApplicationAsync(default!, default);
    }

    [Fact]
    public async Task DecideAsync_WhenRejectionAllowed_CompletesSameCanonicalDecision()
    {
        var fixture = CreateFixture();
        fixture.Repository.GetAsync(CommandId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ReviewDetail());
        fixture.Staged.GetDecisionAsync(CommandId, Arg.Any<CancellationToken>())
            .Returns(AwaitingDecision());
        fixture.Staged.CompleteDecisionRejectedAsync(CommandId, "ID_NOT_VALID", null, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(CompletedDecision(StatutoryDiscountDecisionV2ResultStates.Rejected) with { ReasonCode = "ID_NOT_VALID" });
        fixture.Repository.RecordReviewCompletionAsync(
                CommandId,
                UserId,
                DeviceBindingId,
                ShiftId,
                EvaluationId,
                "REJECT",
                "ID_NOT_VALID",
                CorrelationId,
                Arg.Any<CancellationToken>())
            .Returns(ReviewDetail(reviewStatus: StatutoryDiscountServiceChannelReviewStatuses.Rejected));

        var result = await fixture.Sut.DecideAsync(DecisionCommand(decision: "REJECT", reason: "ID_NOT_VALID"), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.CurrentDecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Rejected);
        await fixture.Staged.Received(1).CompleteDecisionRejectedAsync(CommandId, "ID_NOT_VALID", null, CorrelationId, Arg.Any<CancellationToken>());
        await fixture.Staged.DidNotReceiveWithAnyArgs().CreateOrResolveApplicationAsync(default!, default);
    }

    [Fact]
    public async Task DecideAsync_WhenOppositeTerminalDecisionExists_ReturnsConflict()
    {
        var fixture = CreateFixture();
        fixture.Repository.GetAsync(CommandId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ReviewDetail(reviewStatus: StatutoryDiscountServiceChannelReviewStatuses.Rejected));
        fixture.Staged.GetDecisionAsync(CommandId, Arg.Any<CancellationToken>())
            .Returns(CompletedDecision(StatutoryDiscountDecisionV2ResultStates.Rejected));

        var result = await fixture.Sut.DecideAsync(DecisionCommand(), CancellationToken.None);

        result.DecisionAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_ALREADY_COMPLETED");
        await fixture.Staged.DidNotReceive().CompleteDecisionApprovedAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<StatutoryDiscountDecisionV2TariffFacts?>(),
            Arg.Any<string?>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideAsync_WhenCanonicalCompletedButReviewLinkPending_RepairsReviewLinkage()
    {
        var fixture = CreateFixture();
        fixture.Repository.GetAsync(CommandId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ReviewDetail());
        fixture.Staged.GetDecisionAsync(CommandId, Arg.Any<CancellationToken>())
            .Returns(CompletedDecision(StatutoryDiscountDecisionV2ResultStates.Approved));
        fixture.Repository.RecordReviewCompletionAsync(
                CommandId,
                UserId,
                DeviceBindingId,
                ShiftId,
                EvaluationId,
                "APPROVE",
                "ELIGIBLE",
                CorrelationId,
                Arg.Any<CancellationToken>())
            .Returns(ReviewDetail(reviewStatus: StatutoryDiscountServiceChannelReviewStatuses.Approved));

        var result = await fixture.Sut.DecideAsync(DecisionCommand(), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.AlreadyDecided.Should().BeTrue();
        result.ReviewStatus.Should().Be(StatutoryDiscountServiceChannelReviewStatuses.Approved);
        await fixture.Repository.Received(1).RecordReviewCompletionAsync(
            CommandId,
            UserId,
            DeviceBindingId,
            ShiftId,
            EvaluationId,
            "APPROVE",
            "ELIGIBLE",
            CorrelationId,
            Arg.Any<CancellationToken>());
    }

    private static TestFixture CreateFixture(bool allowed = true)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed));
        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with { Persisted = true });
        var repository = Substitute.For<IStatutoryDiscountServiceChannelReviewRepository>();
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        var sut = new OperatorConsoleServiceChannelStatutoryDiscountReviewService(accessService, accessWriter, repository, staged);
        return new TestFixture(accessService, repository, staged, sut);
    }

    private static OperatorConsoleReviewAccessContext AccessContext() =>
        new(UserId, DeviceBindingId, ShiftId, SiteId, SiteGroupId, CorrelationId, "review-access-key");

    private static StatutoryDiscountServiceChannelReviewDecisionCommand DecisionCommand(
        string decision = "APPROVE",
        string? reason = "ELIGIBLE") =>
        new(
            CommandId,
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            reason,
            DecisionNotes: null,
            ReviewerAttestation: true,
            "review-decision-key",
            CorrelationId);

    private static OperatorConsoleAccessEvaluationResult AccessResult(bool allowed) =>
        new(
            EvaluationId,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            allowed ? [] : ["NO_REVIEW_PERMISSION"],
            "SUPERVISOR",
            new OperatorConsoleDeviceTrustResult(DeviceBindingId, "ACTIVE", "TRUSTED", Trusted: true),
            new OperatorConsoleShiftContextResult(ShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: true),
            Now,
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                HrIdentityMappingId: null,
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.DecideStatutoryDiscount,
                OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow,
                "STATUTORY_DISCOUNT_DECISION",
                CommandId));

    private static StatutoryDiscountServiceChannelReviewQueueItem QueueItem() =>
        new(
            CommandId,
            ParkingSessionId,
            StatutoryDiscountSourceChannels.WebPay,
            SiteId,
            SiteGroupId,
            "TICKET-001",
            "ABC1234",
            "SENIOR_CITIZEN",
            StatutoryDiscountDecisionV2CommandStates.AwaitingReview,
            StatutoryDiscountDecisionV2ResultStates.NotDecided,
            StatutoryDiscountServiceChannelReviewStatuses.PendingReview,
            true,
            true,
            TariffSnapshotId,
            Now,
            CorrelationId);

    private static StatutoryDiscountServiceChannelReviewDetail ReviewDetail(
        string reviewStatus = StatutoryDiscountServiceChannelReviewStatuses.PendingReview) =>
        new(
            CommandId,
            null,
            RequestReference,
            ParkingSessionId,
            StatutoryDiscountSourceChannels.WebPay,
            SiteId,
            SiteGroupId,
            "TICKET-001",
            "ABC1234",
            "SENIOR_CITIZEN",
            StatutoryDiscountDecisionV2CommandStates.AwaitingReview,
            StatutoryDiscountDecisionV2ResultStates.NotDecided,
            reviewStatus,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            "SC-****-1234",
            [new StatutoryDiscountServiceChannelReviewEvidenceFact(
                "SENIOR_CITIZEN_ID",
                "MANUAL_REFERENCE",
                "evidence-ref-001",
                "SC-****-1234",
                "SUBMITTED")],
            true,
            "attested",
            "CUSTOMER_REQUEST",
            true,
            true,
            TariffSnapshotId,
            12500,
            11161,
            1339,
            2232,
            8929,
            "PHP",
            reviewStatus == StatutoryDiscountServiceChannelReviewStatuses.PendingReview ? null : UserId,
            reviewStatus == StatutoryDiscountServiceChannelReviewStatuses.PendingReview ? null : EvaluationId,
            reviewStatus switch
            {
                StatutoryDiscountServiceChannelReviewStatuses.Approved => "APPROVE",
                StatutoryDiscountServiceChannelReviewStatuses.Rejected => "REJECT",
                _ => null
            },
            reviewStatus == StatutoryDiscountServiceChannelReviewStatuses.Rejected ? "DOCUMENT_INVALID" : null,
            Now,
            null,
            CorrelationId);

    private static StatutoryDiscountDecisionV2Record AwaitingDecision() =>
        CompletedDecision(StatutoryDiscountDecisionV2ResultStates.NotDecided) with
        {
            CommandStatus = StatutoryDiscountDecisionV2CommandStates.AwaitingReview,
            DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.NotDecided,
            ResultClassification = StatutoryDiscountOneShotResultClassifications.AwaitingReview,
            CompletedAt = null,
            DecidedAt = null
        };

    private static StatutoryDiscountServiceChannelValidationLinkage ValidationLinkage() =>
        new(
            CommandId,
            Guid.Parse("8a000000-0000-0000-0000-00000000000c"),
            ParkingSessionId,
            "SENIOR_CITIZEN",
            TariffSnapshotId,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            "NATIONAL_LAW_FALLBACK",
            LocalOrdinanceApplied: false,
            12500,
            "PHP",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            "VAT_EXCLUSIVE");

    private static StatutoryDiscountDecisionV2Record CompletedDecision(string result) =>
        new(
            CommandId,
            RequestReference,
            ParkingSessionId,
            StatutoryDiscountSourceChannels.WebPay,
            "SENIOR_CITIZEN",
            $"statutory-discount-decision:{ParkingSessionId:N}:SENIOR_CITIZEN",
            $"statutory-discount-decision:{ParkingSessionId:N}:SENIOR_CITIZEN",
            "decision-v2:sha256:test",
            StatutoryDiscountDecisionV2SemanticHash.SourceVersion,
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            StatutoryDiscountDecisionV2CommandStates.Completed,
            result,
            result == StatutoryDiscountDecisionV2ResultStates.Approved
                ? StatutoryDiscountDecisionClientResultStatuses.Approved
                : StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved,
            Retryable: false,
            StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            SafeErrorCode: null,
            StatutoryDiscountValidationId: null,
            TariffSnapshotId,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            PolicyResolutionBasis: null,
            LocalOrdinanceApplied: false,
            12500,
            11161,
            1339,
            2232,
            8929,
            "PHP",
            EvidenceRequired: true,
            EvidenceRecorded: true,
            ReasonCode: null,
            CorrelationId,
            Now,
            ProcessingStartedAt: null,
            DecidedAt: Now,
            CompletedAt: Now,
            FailedAt: null,
            UpdatedAt: Now);

    private sealed record TestFixture(
        IOperatorConsoleAccessEvaluationService AccessService,
        IStatutoryDiscountServiceChannelReviewRepository Repository,
        IStatutoryDiscountStagedCommandService Staged,
        OperatorConsoleServiceChannelStatutoryDiscountReviewService Sut);
}
