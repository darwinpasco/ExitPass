using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount validation decision behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDecisionServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("49000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("49000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("49000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("49000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("49000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("49000000-0000-0000-0000-000000000006");
    private static readonly Guid DraftId = Guid.Parse("49000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("49000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("49000000-0000-0000-0000-000000000009");

    /// <summary>
    /// Verifies access denial is persisted and prevents decision persistence.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenAccessDenied_DoesNotPersistDecision()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessPersisted.Should().BeTrue();
        result.DecisionAccepted.Should().BeFalse();
        result.DecisionPersisted.Should().BeFalse();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies an allowed approval persists an approved review status.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenApprovalAllowed_PersistsApprovedStatus()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("APPROVE", "REQUESTED", "APPROVED", decisionChanged: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DecisionAccepted.Should().BeTrue();
        result.DecisionPersisted.Should().BeTrue();
        result.PreviousValidationStatus.Should().Be("REQUESTED");
        result.CurrentValidationStatus.Should().Be("APPROVED");
        result.DecisionChanged.Should().BeTrue();

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(request =>
                request.DraftId == DraftId &&
                request.Decision == "APPROVE" &&
                request.TargetValidationStatus == "APPROVED" &&
                request.DecidedByUserId == UserId &&
                request.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies reject requires and preserves a reason code.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenRejectAllowed_PersistsRejectedStatusWithReason()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("REJECT", "REQUESTED", "REJECTED", decisionChanged: true, reason: "ID_NOT_VALID"));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(decision: "REJECT", reason: "ID_NOT_VALID"), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.CurrentValidationStatus.Should().Be("REJECTED");
        result.DecisionReasonCode.Should().Be("ID_NOT_VALID");
    }

    /// <summary>
    /// Verifies same terminal decision replay is deterministic.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenSameDecisionAlreadyTerminal_ReturnsAlreadyDecided()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("APPROVE", "APPROVED", "APPROVED", alreadyDecided: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.DecisionPersisted.Should().BeTrue();
        result.AlreadyDecided.Should().BeTrue();
        result.DecisionChanged.Should().BeFalse();
    }

    /// <summary>
    /// Verifies evidence-required approvals can be blocked without mutating decision status.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenEvidenceRequiredNotCaptured_ReturnsIneligible()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDecisionPersistenceResult(
                Found: true,
                DecisionAccepted: false,
                DecisionPersisted: false,
                DraftId,
                ParkingSessionId,
                "SENIOR_CITIZEN",
                "REQUESTED",
                "REQUESTED",
                "APPROVE",
                DecisionReasonCode: null,
                AlreadyDecided: false,
                DecisionChanged: false,
                IneligibilityReason: "EVIDENCE_REQUIRED_NOT_CAPTURED",
                ErrorCode: "EVIDENCE_REQUIRED_NOT_CAPTURED"));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.DecisionAccepted.Should().BeFalse();
        result.DecisionPersisted.Should().BeFalse();
        result.ErrorCode.Should().Be("EVIDENCE_REQUIRED_NOT_CAPTURED");
    }

    /// <summary>
    /// Verifies unsupported decisions are rejected before persistence.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenDecisionUnsupported_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DecideAsync(Command(decision: "ESCALATE"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Decision must be APPROVE or REJECT*");
    }

    /// <summary>
    /// Verifies reject decisions require a reason.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenRejectReasonMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DecideAsync(Command(decision: "REJECT", reason: null), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*DecisionReasonCode is required for REJECT*");
    }

    /// <summary>
    /// Verifies reviewer attestation is required.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenReviewerAttestationFalse_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DecideAsync(Command(reviewerAttestation: false), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ReviewerAttestation must be true*");
    }

    private static OperatorConsoleStatutoryDiscountDecisionService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleStatutoryDiscountDecisionWriter? decisionWriter = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        decisionWriter ??= Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();

        return new OperatorConsoleStatutoryDiscountDecisionService(
            accessService,
            accessWriter,
            decisionWriter);
    }

    private static OperatorConsoleStatutoryDiscountDecisionCommand Command(
        string decision = "APPROVE",
        string? reason = null,
        bool reviewerAttestation = true) =>
        new(
            DraftId,
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            reason,
            DecisionNotes: "Manual review decision.",
            reviewerAttestation,
            "operator-console-statutory-discount-decision-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionPersistenceResult Persisted(
        string decision,
        string previousStatus,
        string currentStatus,
        bool decisionChanged = false,
        bool alreadyDecided = false,
        string? reason = null) =>
        new(
            Found: true,
            DecisionAccepted: true,
            DecisionPersisted: true,
            DraftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            previousStatus,
            currentStatus,
            decision,
            reason,
            alreadyDecided,
            decisionChanged,
            IneligibilityReason: null,
            ErrorCode: null);

    private static OperatorConsoleAccessEvaluationResult AccessResult(
        bool allowed,
        IReadOnlyList<string> reasons) =>
        new(
            Guid.Empty,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            reasons,
            allowed ? "OPERATOR" : null,
            new OperatorConsoleDeviceTrustResult(DeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(ShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"),
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                Guid.Parse("49000000-0000-0000-0000-000000000010"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                "SUBMIT_DECISION",
                "STATUTORY_DISCOUNT_VALIDATION",
                TargetEntityType: null,
                TargetEntityId: null));
}
