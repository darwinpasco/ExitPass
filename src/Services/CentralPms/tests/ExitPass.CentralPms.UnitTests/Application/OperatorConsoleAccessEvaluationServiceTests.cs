using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests read-only Operator Console access evaluation rules.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-29T08:00:00Z");
    private static readonly Guid UserId = Guid.Parse("44000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceBindingId = Guid.Parse("44000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("44000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("44000000-0000-0000-0000-000000000004");
    private static readonly Guid ShiftId = Guid.Parse("44000000-0000-0000-0000-000000000005");
    private static readonly Guid CorrelationId = Guid.Parse("44000000-0000-0000-0000-000000000006");
    private static readonly Guid HrIdentityMappingId = Guid.Parse("44000000-0000-0000-0000-000000000007");

    /// <summary>
    /// Verifies a complete supported read model allows the action without persistence.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_WhenContextPassesAllRules_AllowsWithoutPersistence()
    {
        var sut = CreateSut(ValidContext);

        var result = await sut.EvaluateAsync(Command(), CancellationToken.None);

        result.Allowed.Should().BeTrue();
        result.Decision.Should().Be("ALLOWED");
        result.DenialReasons.Should().BeEmpty();
        result.EffectiveRole.Should().Be("OPERATOR");
        result.DeviceTrust.Trusted.Should().BeTrue();
        result.ShiftContext.Active.Should().BeTrue();
        result.SiteContext.Assigned.Should().BeTrue();
        result.Persisted.Should().BeFalse();
        result.CorrelationId.Should().Be(CorrelationId);
    }

    /// <summary>
    /// Verifies supported rule failures produce stable denial reason codes.
    /// </summary>
    [Theory]
    [MemberData(nameof(DenialCases))]
    public async Task EvaluateAsync_WhenRuleFails_ReturnsExpectedReason(
        Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext> contextFactory,
        OperatorConsoleAccessEvaluationCommand command,
        string expectedReason)
    {
        var sut = CreateSut(contextFactory);

        var result = await sut.EvaluateAsync(command, CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.Decision.Should().Be("DENIED");
        result.DenialReasons.Should().Contain(expectedReason);
        result.Persisted.Should().BeFalse();
    }

    /// <summary>
    /// Denial cases for MVP evaluator rules.
    /// </summary>
    public static IEnumerable<object[]> DenialCases()
    {
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)MissingHrMapping, Command(), "HR_IDENTITY_MAPPING_NOT_FOUND"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)InactiveHrMapping, Command(), "HR_IDENTITY_MAPPING_INACTIVE"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)MissingDeviceBinding, Command(), "DEVICE_BINDING_NOT_FOUND"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)InactiveDeviceBinding, Command(), "DEVICE_BINDING_INACTIVE"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)UntrustedDevice, Command(), "DEVICE_NOT_TRUSTED"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)MissingDeviceAssignment, Command(), "DEVICE_SITE_ASSIGNMENT_NOT_FOUND"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)InvalidDeviceAssignment, Command(), "DEVICE_SITE_ASSIGNMENT_INVALID"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)MissingShift, Command(), "NO_ACTIVE_SHIFT"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)RevokedShift, Command(), "SHIFT_REVOKED"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)ActiveConflictingTakeover, Command(), "SHIFT_TAKEOVER_ACTIVE"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)ValidContext, Command(workflowCode: "UNKNOWN_WORKFLOW"), "WORKFLOW_NOT_SUPPORTED"];
        yield return [(Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext>)ValidContext, Command(actionCode: "UNKNOWN_ACTION"), "ACTION_NOT_SUPPORTED"];
    }

    private static OperatorConsoleAccessEvaluationService CreateSut(
        Func<OperatorConsoleAccessEvaluationReadRequest, OperatorConsoleAccessEvaluationReadContext> contextFactory)
    {
        var repository = Substitute.For<IOperatorConsoleAccessEvaluationReadRepository>();
        repository.LoadAsync(Arg.Any<OperatorConsoleAccessEvaluationReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => contextFactory(call.ArgAt<OperatorConsoleAccessEvaluationReadRequest>(0)));

        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        return new OperatorConsoleAccessEvaluationService(repository, clock);
    }

    private static OperatorConsoleAccessEvaluationCommand Command(
        string workflowCode = "STATUTORY_DISCOUNT_VALIDATION",
        string actionCode = "START_WORKFLOW") =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            workflowCode,
            actionCode,
            ParkingSessionId: null,
            EvidenceAccessIntent: null,
            "operator-console-evaluator-test",
            CorrelationId);

    private static OperatorConsoleAccessEvaluationReadContext ValidContext(OperatorConsoleAccessEvaluationReadRequest request) =>
        new(
            request,
            new OperatorHrIdentityMappingReadModel(
                HrIdentityMappingId,
                UserId,
                "MOCK_HR",
                "ACTIVE",
                Now.AddHours(-8),
                Now.AddHours(8),
                RevokedAt: null,
                RevocationReasonCode: null),
            new OperatorDeviceBindingReadModel(
                DeviceBindingId,
                "OC-DEVICE-001",
                "Operator Console Device",
                SiteGroupId,
                SiteId,
                ServiceIdentityId: null,
                "ACTIVE",
                "BROWSER_KEY_AND_MTLS",
                "TEST",
                LastSeenAt: Now,
                RevokedAt: null,
                RevocationReasonCode: null),
            new OperatorDeviceAssignmentReadModel(
                Guid.Parse("44000000-0000-0000-0000-000000000008"),
                DeviceBindingId,
                SiteGroupId,
                SiteId,
                "ACTIVE",
                "TEST",
                Now.AddHours(-8),
                Now.AddHours(8),
                EndedAt: null),
            new OperatorShiftReadModel(
                ShiftId,
                HrIdentityMappingId,
                UserId,
                SiteGroupId,
                SiteId,
                "MOCK_HR",
                "ACTIVE",
                Now.AddHours(-1),
                Now.AddHours(7),
                Now.AddHours(-1),
                Now.AddHours(7),
                RevokedAt: null,
                RevocationReasonCode: null,
                CurrentTakeoverId: null),
            LatestShiftVersion: null,
            LatestShiftRevocation: null,
            ActiveShiftTakeover: null,
            StatutoryEntitlementFingerprint: null);

    private static OperatorConsoleAccessEvaluationReadContext MissingHrMapping(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with { HrIdentityMapping = null };

    private static OperatorConsoleAccessEvaluationReadContext InactiveHrMapping(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with
        {
            HrIdentityMapping = ValidContext(request).HrIdentityMapping! with { MappingStatus = "SUSPENDED" }
        };

    private static OperatorConsoleAccessEvaluationReadContext MissingDeviceBinding(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with { DeviceBinding = null };

    private static OperatorConsoleAccessEvaluationReadContext InactiveDeviceBinding(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with
        {
            DeviceBinding = ValidContext(request).DeviceBinding! with { DeviceStatus = "SUSPENDED" }
        };

    private static OperatorConsoleAccessEvaluationReadContext UntrustedDevice(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with
        {
            DeviceBinding = ValidContext(request).DeviceBinding! with { TrustLevel = "UNVERIFIED" }
        };

    private static OperatorConsoleAccessEvaluationReadContext MissingDeviceAssignment(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with { DeviceAssignment = null };

    private static OperatorConsoleAccessEvaluationReadContext InvalidDeviceAssignment(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with
        {
            DeviceAssignment = ValidContext(request).DeviceAssignment! with { AssignmentStatusCode = "ENDED" }
        };

    private static OperatorConsoleAccessEvaluationReadContext MissingShift(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with { ActiveShift = null };

    private static OperatorConsoleAccessEvaluationReadContext RevokedShift(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with
        {
            ActiveShift = ValidContext(request).ActiveShift! with { OperationalStatus = "REVOKED", RevokedAt = Now }
        };

    private static OperatorConsoleAccessEvaluationReadContext ActiveConflictingTakeover(OperatorConsoleAccessEvaluationReadRequest request) =>
        ValidContext(request) with
        {
            ActiveShiftTakeover = new OperatorShiftTakeoverReadModel(
                Guid.Parse("44000000-0000-0000-0000-000000000009"),
                ShiftId,
                UserId,
                Guid.Parse("44000000-0000-0000-0000-000000000010"),
                "ACTIVE",
                "SUPERVISOR_TAKEOVER",
                SiteId,
                Now.AddMinutes(-30),
                Now.AddMinutes(-20),
                Now.AddMinutes(-20),
                Now.AddHours(1),
                EndedAt: null)
        };
}
