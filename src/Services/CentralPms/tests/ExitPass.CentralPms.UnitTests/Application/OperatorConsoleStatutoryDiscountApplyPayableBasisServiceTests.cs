using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount payable-basis application behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountApplyPayableBasisServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("4b000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("4b000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("4b000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("4b000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("4b000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("4b000000-0000-0000-0000-000000000006");
    private static readonly Guid ValidationId = Guid.Parse("4b000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("4b000000-0000-0000-0000-000000000008");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("4b000000-0000-0000-0000-000000000009");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("4b000000-0000-0000-0000-00000000000a");
    private static readonly Guid ApplicationId = Guid.Parse("4b000000-0000-0000-0000-00000000000b");
    private static readonly Guid CorrelationId = Guid.Parse("4b000000-0000-0000-0000-00000000000c");

    /// <summary>
    /// Verifies access denial is persisted and prevents payable-basis application.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenAccessDenied_DoesNotCallWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), writer);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessPersisted.Should().BeTrue();
        result.ApplicationAccepted.Should().BeFalse();
        result.ApplicationPersisted.Should().BeFalse();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    /// <summary>
    /// Verifies allowed access delegates payable-basis application to the writer.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenAccessAllowed_ReturnsPersistedApplication()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        writer.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(PersistedApplication(alreadyApplied: false));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.ApplicationAccepted.Should().BeTrue();
        result.ApplicationPersisted.Should().BeTrue();
        result.PayableBasisApplicationId.Should().Be(ApplicationId);
        result.ApplicationStatus.Should().Be("APPLIED");
        result.GrossAmountMinorUnits.Should().Be(12500);
        result.VatAmountMinorUnits.Should().Be(1339);
        result.VatExclusiveAmountMinorUnits.Should().Be(11161);
        result.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        result.FinalPayableAmountMinorUnits.Should().Be(8929);
        result.PolicySnapshotUsed.Should().BeTrue();
        result.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");

        await writer.Received(1).ApplyAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(request =>
                request.ValidationId == ValidationId &&
                request.OriginalTariffSnapshotId == OriginalTariffSnapshotId &&
                request.AppliedByUserId == UserId &&
                request.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies replayed applications surface deterministic already-applied state.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenWriterReturnsExistingApplication_SurfacesAlreadyApplied()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        writer.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(PersistedApplication(alreadyApplied: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeTrue();
        result.ApplicationPersisted.Should().BeTrue();
        result.AlreadyApplied.Should().BeTrue();
        result.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
    }

    /// <summary>
    /// Verifies request validation runs before access evaluation.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenValidationIdMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.ApplyAsync(Command(validationId: Guid.Empty), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ValidationId is required*");
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter? writer = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        writer ??= Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();

        return new OperatorConsoleStatutoryDiscountApplyPayableBasisService(
            accessService,
            accessWriter,
            writer);
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisCommand Command(Guid? validationId = null) =>
        new(
            validationId ?? ValidationId,
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            OriginalTariffSnapshotId,
            "operator-console-statutory-discount-apply-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult PersistedApplication(bool alreadyApplied) =>
        new(
            ApplicationAccepted: true,
            ApplicationPersisted: true,
            ApplicationId,
            ValidationId,
            ParkingSessionId,
            OriginalTariffSnapshotId,
            AppliedTariffSnapshotId,
            "APPLIED",
            alreadyApplied,
            GrossAmountMinorUnits: 12500,
            VatAmountMinorUnits: 1339,
            VatExclusiveAmountMinorUnits: 11161,
            StatutoryDiscountAmountMinorUnits: 2232,
            FinalPayableAmountMinorUnits: 8929,
            CurrencyCode: "PHP",
            StatutoryDiscountPolicyId: Guid.Parse("4b000000-0000-0000-0000-00000000000e"),
            ResolvedJurisdictionId: Guid.Parse("4b000000-0000-0000-0000-00000000000f"),
            PolicyResolutionBasis: "NATIONAL_LAW_FALLBACK",
            PolicyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            BenefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
            NationalLawReference: "RA 9994",
            OrdinanceReference: null,
            PolicySnapshotUsed: true,
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
                Guid.Parse("4b000000-0000-0000-0000-00000000000d"),
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
