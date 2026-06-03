using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated metadata-only Operator Console statutory discount evidence intake behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountEvidenceServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("66000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("66000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("66000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("66000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("66000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("66000000-0000-0000-0000-000000000006");
    private static readonly Guid DraftId = Guid.Parse("66000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("66000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("66000000-0000-0000-0000-000000000009");
    private static readonly Guid EvidenceId = Guid.Parse("66000000-0000-0000-0000-000000000010");

    [Fact]
    public async Task CaptureAsync_WhenAllowed_PersistsMetadataOnlyEvidence()
    {
        var repository = Repository();
        repository.CaptureAsync(Arg.Any<OperatorConsoleStatutoryDiscountEvidencePersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(CaptureResult());
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.CaptureAsync(Command(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.EvidenceId.Should().Be(EvidenceId);
        result.EvidenceRequiredSatisfied.Should().BeTrue();
        await repository.Received(1).CaptureAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountEvidencePersistenceCommand>(command =>
                command.DraftId == DraftId &&
                command.EvidenceType == "SENIOR_CITIZEN_ID" &&
                command.CaptureMethod == "OPERATOR_CONFIRMED" &&
                command.CapturedByUserId == UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_WhenDraftMissing_ReturnsNull()
    {
        var repository = Repository(missing: true);
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.CaptureAsync(Command(), CancellationToken.None);

        result.Should().BeNull();
        await repository.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default);
    }

    [Fact]
    public async Task CaptureAsync_WhenConfirmationMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []), Repository());

        var action = () => sut.CaptureAsync(Command(operatorConfirmation: false), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*OperatorConfirmation must be true*");
    }

    [Fact]
    public async Task CaptureAsync_WhenEvidenceTypeDoesNotMatchEntitlement_ThrowsValidationError()
    {
        var repository = Repository();
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var action = () => sut.CaptureAsync(Command(evidenceType: "OTHER_SUPPORTING_DOCUMENT"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EvidenceType must match required evidence type for SENIOR_CITIZEN*");
        await repository.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default);
    }

    [Fact]
    public async Task CaptureAsync_WhenAccessDenied_DoesNotPersistEvidence()
    {
        var repository = Repository();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), repository);

        var result = await sut.CaptureAsync(Command(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.AccessAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCESS_DENIED");
        await repository.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default);
    }

    [Fact]
    public async Task ListAsync_WhenAllowed_ReturnsEvidenceSummary()
    {
        var repository = Repository();
        repository.ListAsync(DraftId, CorrelationId, Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountEvidenceListResult(
                DraftId,
                EvidenceRequired: true,
                EvidenceRequiredSatisfied: true,
                ["SENIOR_CITIZEN_ID"],
                EvidenceCount: 1,
                LatestEvidenceStatus: "CAPTURED",
                [Metadata()],
                CorrelationId));
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.ListAsync(ListQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.EvidenceRequiredSatisfied.Should().BeTrue();
        result.Items.Should().ContainSingle();
    }

    private static OperatorConsoleStatutoryDiscountEvidenceService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleStatutoryDiscountEvidenceRepository repository)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult);

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });

        return new OperatorConsoleStatutoryDiscountEvidenceService(accessService, accessWriter, repository);
    }

    private static IOperatorConsoleStatutoryDiscountEvidenceRepository Repository(bool missing = false)
    {
        var repository = Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceRepository>();
        repository.GetDraftContextAsync(DraftId, Arg.Any<CancellationToken>())
            .Returns(missing ? null : new OperatorConsoleStatutoryDiscountEvidenceDraftContext(
                DraftId,
                ParkingSessionId,
                SiteId,
                SiteGroupId,
                "SENIOR_CITIZEN",
                "REQUESTED",
                EvidenceRequired: true,
                EvidenceCaptured: false));

        return repository;
    }

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureCommand Command(
        bool operatorConfirmation = true,
        string evidenceType = "SENIOR_CITIZEN_ID") =>
        new(
            DraftId,
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            evidenceType,
            "OPERATOR_CONFIRMED",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            StorageReference: null,
            ReferenceNumber: null,
            Notes: null,
            operatorConfirmation,
            "evidence-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceListQuery ListQuery() =>
        new(DraftId, UserId, DeviceBindingId, SiteId, SiteGroupId, ShiftId, CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureResult CaptureResult() =>
        new(
            EvidenceId,
            DraftId,
            "SENIOR_CITIZEN_ID",
            "OPERATOR_CONFIRMED",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            "operator-confirmed",
            ReferenceNumberMasked: null,
            UserId,
            DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            "NOT_REDACTED",
            "CAPTURED",
            EvidenceRequiredSatisfied: true,
            CurrentDraftStatus: "REQUESTED",
            AccessAllowed: true,
            ErrorCode: null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceMetadataResult Metadata() =>
        new(
            EvidenceId,
            DraftId,
            "SENIOR_CITIZEN_ID",
            "OPERATOR_CONFIRMED",
            "operator-confirmed",
            UserId,
            DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            "NOT_REDACTED",
            "CAPTURED",
            CorrelationId);

    private static OperatorConsoleAccessEvaluationResult AccessResult(bool allowed, IReadOnlyList<string> denialReasons) =>
        new(
            EvaluationId,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            denialReasons,
            EffectiveRole: allowed ? "OPERATOR" : null,
            DeviceTrust: null,
            ShiftContext: null,
            SiteContext: null,
            DateTimeOffset.Parse("2026-06-03T10:00:00+08:00"),
            Persisted: true,
            CorrelationId,
            PersistenceContext: null);
}
