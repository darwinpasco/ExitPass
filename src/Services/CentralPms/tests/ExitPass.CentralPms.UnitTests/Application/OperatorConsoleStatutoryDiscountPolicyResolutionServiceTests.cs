using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount policy resolution behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountPolicyResolutionServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("6d000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("6d000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("6d000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("6d000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("6d000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("6d000000-0000-0000-0000-000000000006");
    private static readonly Guid PolicyId = Guid.Parse("6d000000-0000-0000-0000-000000000007");
    private static readonly Guid JurisdictionId = Guid.Parse("6d000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("6d000000-0000-0000-0000-000000000009");

    /// <summary>
    /// Verifies access denial is persisted and prevents policy repository access.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenAccessDenied_DoesNotResolvePolicy()
    {
        var repository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), repository);

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessPersisted.Should().BeTrue();
        result.PolicyResolved.Should().BeFalse();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");
        await repository.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    /// <summary>
    /// Verifies allowed access delegates read-only policy resolution.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenAccessAllowed_ReturnsPolicy()
    {
        var repository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        repository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: true,
                Policy(),
                SiteId,
                SiteGroupId,
                JurisdictionId,
                IneligibilityReason: null,
                ErrorCode: null));

        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.PolicyResolved.Should().BeTrue();
        result.Policy.Should().NotBeNull();
        result.Policy!.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");
        result.Policy.NationalLawReference.Should().Be("RA 9994");

        await repository.Received(1).ResolveAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(request =>
                request.SiteId == SiteId &&
                request.SiteGroupId == SiteGroupId &&
                request.EntitlementType == "SENIOR_CITIZEN" &&
                request.EffectiveDate == new DateOnly(2026, 5, 30)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies unsupported entitlement types fail before access evaluation.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenEntitlementUnsupported_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.ResolveAsync(Command(entitlementType: "OTHER_STATUTORY"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EntitlementType must be SENIOR_CITIZEN or PWD*");
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository? repository = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        repository ??= Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();

        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-05-30T00:00:00Z"));

        return new OperatorConsoleStatutoryDiscountPolicyResolutionService(
            accessService,
            accessWriter,
            repository,
            clock);
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionCommand Command(string entitlementType = "SENIOR_CITIZEN") =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId: null,
            entitlementType,
            "operator-console-policy-resolution-test",
            CorrelationId);

    private static OperatorConsoleResolvedStatutoryDiscountPolicy Policy() =>
        new(
            PolicyId,
            JurisdictionId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "RA 9994 Senior Citizen National Fallback",
            "NATIONAL_LAW_FALLBACK",
            "NATIONAL_LAW",
            "LEGAL_REFERENCE",
            "Expanded Senior Citizens Act of 2010",
            OrdinanceReference: null,
            "RA 9994",
            "VERIFIED_OFFICIAL",
            "NON_RESIDENT_ALLOWED",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            FreeDurationMinutes: null,
            InitialRateExempt: false,
            FullFeeExempt: false,
            OvernightExcluded: false,
            ValetExcluded: false,
            StandaloneParkingExcluded: false,
            DriverOrPassengerRequired: false,
            "NOT_APPLICABLE",
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "CHARGEABLE_PORTION_ONLY",
            "NO_STACKING_ON_FREE_PERIOD",
            "NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY",
            RequiresOperatorValidation: true,
            RequiresEvidence: true,
            new DateOnly(2026, 1, 1),
            EffectiveTo: null,
            SourceReference: null,
            JsonSerializer.SerializeToElement(new { policyCode = "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK" }));

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
            DateTimeOffset.Parse("2026-05-30T08:00:00Z"),
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                Guid.Parse("6d000000-0000-0000-0000-00000000000a"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.ViewPolicyResolution,
                "STATUTORY_DISCOUNT_VALIDATION",
                TargetEntityType: null,
                TargetEntityId: null));
}
