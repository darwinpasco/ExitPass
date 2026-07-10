using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests the Operator Console access readiness backend foundation.
/// </summary>
public sealed class OperatorConsoleAccessReadinessServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-08T00:00:00Z");
    private static readonly Guid OperatorUserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
    private static readonly Guid DeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
    private static readonly Guid ShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");
    private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("52883917-a776-4656-8d0a-b87087d646b1");

    [Fact]
    public void ActionCatalog_IncludesRequiredActionCodes()
    {
        var catalog = new OperatorConsoleActionCatalog();

        var codes = catalog.GetAll().Select(entry => entry.Code);

        codes.Should().Contain(new[]
        {
            OperatorConsoleActionCodes.SessionLookup,
            OperatorConsoleActionCodes.CreateStatutoryDiscountDraft,
            OperatorConsoleActionCodes.ViewStatutoryDiscountDraft,
            OperatorConsoleActionCodes.DecideStatutoryDiscount,
            OperatorConsoleActionCodes.CaptureEvidence,
            OperatorConsoleActionCodes.ViewEvidence,
            OperatorConsoleActionCodes.ApplyStatutoryDiscountPayableBasis,
            OperatorConsoleActionCodes.ViewPolicyResolution,
            OperatorConsoleActionCodes.SupervisorReview,
            OperatorConsoleActionCodes.SupervisorOverride,
            OperatorConsoleActionCodes.ViewAuditReport,
            OperatorConsoleActionCodes.VoidFiscalDocument
        });
    }

    [Fact]
    public void DenialReasonCatalog_IncludesRequiredReasonCodes()
    {
        var catalog = new OperatorConsoleDenialReasonCatalog();

        var codes = catalog.GetAll().Select(entry => entry.Code);

        codes.Should().Contain(new[]
        {
            OperatorConsoleDenialReasonCatalog.OperatorIdMissing,
            OperatorConsoleDenialReasonCatalog.OperatorNotFound,
            OperatorConsoleDenialReasonCatalog.OperatorInactive,
            OperatorConsoleDenialReasonCatalog.RoleNotAllowed,
            OperatorConsoleDenialReasonCatalog.DeviceIdMissing,
            OperatorConsoleDenialReasonCatalog.DeviceNotEnrolled,
            OperatorConsoleDenialReasonCatalog.DeviceNotActive,
            OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch,
            OperatorConsoleDenialReasonCatalog.ShiftIdMissing,
            OperatorConsoleDenialReasonCatalog.ShiftNotFound,
            OperatorConsoleDenialReasonCatalog.ShiftNotActive,
            OperatorConsoleDenialReasonCatalog.ShiftSiteMismatch,
            OperatorConsoleDenialReasonCatalog.SiteIdMissing,
            OperatorConsoleDenialReasonCatalog.SiteGroupIdMissing,
            OperatorConsoleDenialReasonCatalog.OperatorSiteNotAllowed,
            OperatorConsoleDenialReasonCatalog.ActionNotAllowedForRole,
            OperatorConsoleDenialReasonCatalog.ActionNotAllowedForWorkflowState,
            OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction,
            OperatorConsoleDenialReasonCatalog.CorrelationIdMissing,
            OperatorConsoleDenialReasonCatalog.AuditPersistenceFailed
        });

        catalog.Find(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction)!.Retryable.Should().BeFalse();
        catalog.Find(OperatorConsoleDenialReasonCatalog.SiteIdMissing)!.UxMessageCategory.Should().Be("SITE_BLOCKED");
    }

    [Fact]
    public void Evaluate_WhenContextPassesFoundationChecks_Allows()
    {
        var sut = CreateSut();

        var result = sut.Evaluate(ValidCommand());

        result.AccessAllowed.Should().BeTrue();
        result.AccessDecision.Should().Be("ALLOWED");
        result.ReadinessStatus.Should().Be("READY");
        result.DenialReasons.Should().BeEmpty();
        result.ReadinessDimensions.Should().OnlyContain(dimension => dimension.Status == "READY");
        result.CorrelationId.Should().Be(CorrelationId);
        result.EvaluatedAt.Should().Be(Now);
    }

    [Fact]
    public void Evaluate_WhenOperatorIdMissing_ReturnsOperatorIdMissing()
    {
        var sut = CreateSut();

        var result = sut.Evaluate(ValidCommand() with { OperatorUserId = null });

        result.AccessAllowed.Should().BeFalse();
        result.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(OperatorConsoleDenialReasonCatalog.OperatorIdMissing);
        result.OperatorReadiness.Ready.Should().BeFalse();
        result.ReadinessDimensions.Single(dimension => dimension.Dimension == "operator")
            .DenialReasonCodes.Should().Contain(OperatorConsoleDenialReasonCatalog.OperatorIdMissing);
    }

    [Fact]
    public void Evaluate_WhenSiteContextMissing_ReturnsSiteReasonCodes()
    {
        var sut = CreateSut();

        var result = sut.Evaluate(ValidCommand() with { SiteId = null, SiteGroupId = null });

        result.AccessAllowed.Should().BeFalse();
        result.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(new[]
            {
                OperatorConsoleDenialReasonCatalog.SiteIdMissing,
                OperatorConsoleDenialReasonCatalog.SiteGroupIdMissing
            });
        result.SiteReadiness.Ready.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_WhenMultipleDimensionsFail_AggregatesDimensionFailures()
    {
        var sut = CreateSut();

        var result = sut.Evaluate(ValidCommand() with
        {
            OperatorUserId = Guid.Empty,
            OperatorDeviceBindingId = null,
            OperatorShiftId = null,
            SiteId = null,
            SiteGroupId = null,
            CorrelationId = Guid.Empty
        });

        result.AccessAllowed.Should().BeFalse();
        result.ReadinessStatus.Should().Be("BLOCKED");
        result.DenialReasons.Select(reason => reason.Code).Should().Contain(new[]
        {
            OperatorConsoleDenialReasonCatalog.OperatorIdMissing,
            OperatorConsoleDenialReasonCatalog.DeviceIdMissing,
            OperatorConsoleDenialReasonCatalog.ShiftIdMissing,
            OperatorConsoleDenialReasonCatalog.SiteIdMissing,
            OperatorConsoleDenialReasonCatalog.SiteGroupIdMissing,
            OperatorConsoleDenialReasonCatalog.CorrelationIdMissing
        });
        result.ReadinessDimensions.Count(dimension => dimension.Status == "BLOCKED").Should().Be(5);
    }

    [Fact]
    public void Evaluate_WhenProductionUsesLocalDevFallback_DeniesFallbackTrust()
    {
        var sut = CreateSut();

        var result = sut.Evaluate(ValidCommand() with
        {
            EnvironmentName = "Production",
            UsesLocalDevFallbackContext = true
        });

        result.AccessAllowed.Should().BeFalse();
        result.AccessDecision.Should().Be("DENIED");
        result.ReadinessStatus.Should().Be("BLOCKED");
        result.Retryable.Should().BeFalse();
        result.NextOperatorAction.Should().Be("Use production device enrollment, active shift, and site readiness records before continuing.");
        result.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
        result.DenialReasons.Single(reason => reason.Code == OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction)
            .Retryable.Should().BeFalse();
        result.ReadinessDimensions.Single(dimension => dimension.Dimension == "localDevBoundary")
            .DenialReasonCodes.Should().Contain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
    }

    [Fact]
    public async Task EvaluateAsync_WhenProductionRepositoryCapabilityMissing_FailsClosed()
    {
        var sut = CreateSut(MissingRepositoryCapabilities());

        var result = await sut.EvaluateAsync(ValidCommand() with { EnvironmentName = "Production" }, CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessDecision.Should().Be("DENIED");
        result.DenialReasons.Select(reason => reason.Code)
            .Should().Contain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNonProductionRepositoryCapabilityMissing_PreservesSandboxFallback()
    {
        var sut = CreateSut(MissingRepositoryCapabilities());

        var result = await sut.EvaluateAsync(ValidCommand() with { EnvironmentName = "Development" }, CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DenialReasons.Select(reason => reason.Code)
            .Should().NotContain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRepositoryReturnsReadinessFailures_MapsStableDenialReasons()
    {
        var sut = CreateSut(new OperatorConsoleAccessReadinessRepositoryResult(
            FullRepositoryCapabilities(),
            [OperatorConsoleDenialReasonCatalog.OperatorNotFound],
            [OperatorConsoleDenialReasonCatalog.DeviceNotEnrolled],
            [OperatorConsoleDenialReasonCatalog.ShiftNotFound],
            [OperatorConsoleDenialReasonCatalog.OperatorSiteNotAllowed]));

        var result = await sut.EvaluateAsync(ValidCommand(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.DenialReasons.Select(reason => reason.Code).Should().Contain(new[]
        {
            OperatorConsoleDenialReasonCatalog.OperatorNotFound,
            OperatorConsoleDenialReasonCatalog.DeviceNotEnrolled,
            OperatorConsoleDenialReasonCatalog.ShiftNotFound,
            OperatorConsoleDenialReasonCatalog.OperatorSiteNotAllowed
        });
        result.OperatorReadiness.Ready.Should().BeFalse();
        result.DeviceReadiness.Ready.Should().BeFalse();
        result.ShiftReadiness.Ready.Should().BeFalse();
        result.SiteReadiness.Ready.Should().BeFalse();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Sandbox")]
    public void Evaluate_WhenNonProductionUsesLocalDevFallback_DoesNotDenyForProductionFallback(string environmentName)
    {
        var sut = CreateSut();

        var result = sut.Evaluate(ValidCommand() with
        {
            EnvironmentName = environmentName,
            UsesLocalDevFallbackContext = true
        });

        result.AccessAllowed.Should().BeTrue();
        result.DenialReasons.Select(reason => reason.Code)
            .Should().NotContain(OperatorConsoleDenialReasonCatalog.LocalDevContextNotAllowedInProduction);
        result.ReadinessDimensions.Single(dimension => dimension.Dimension == "localDevBoundary")
            .Status.Should().Be("READY");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Sandbox")]
    public void LocalDevFallbackPolicy_WhenNonProduction_AllowsFallbackTrust(string environmentName)
    {
        OperatorConsoleLocalDevFallbackPolicy.IsFallbackAllowed(environmentName).Should().BeTrue();
        OperatorConsoleLocalDevFallbackPolicy.ShouldDenyFallback(environmentName, usesLocalDevFallbackContext: true).Should().BeFalse();
    }

    [Fact]
    public void LocalDevFallbackPolicy_WhenProduction_DeniesFallbackTrust()
    {
        OperatorConsoleLocalDevFallbackPolicy.IsFallbackAllowed("Production").Should().BeFalse();
        OperatorConsoleLocalDevFallbackPolicy.ShouldDenyFallback("Production", usesLocalDevFallbackContext: true).Should().BeTrue();
    }

    private static OperatorConsoleAccessReadinessService CreateSut()
    {
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        return new OperatorConsoleAccessReadinessService(
            new OperatorConsoleActionCatalog(),
            new OperatorConsoleDenialReasonCatalog(),
            clock);
    }

    private static OperatorConsoleAccessReadinessService CreateSut(
        OperatorConsoleAccessReadinessRepositoryResult repositoryResult)
    {
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        var repository = Substitute.For<IOperatorConsoleAccessReadinessRepository>();
        repository.LoadAsync(Arg.Any<OperatorConsoleAccessReadinessCommand>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(repositoryResult);

        return new OperatorConsoleAccessReadinessService(
            new OperatorConsoleActionCatalog(),
            new OperatorConsoleDenialReasonCatalog(),
            clock,
            repository);
    }

    private static OperatorConsoleAccessReadinessCommand ValidCommand() =>
        new(
            OperatorUserId,
            DeviceBindingId,
            ShiftId,
            SiteId,
            SiteGroupId,
            OperatorConsoleActionCodes.DecideStatutoryDiscount,
            TargetEntityType: "STATUTORY_DISCOUNT_VALIDATION",
            TargetEntityId: Guid.Parse("b84541dc-4929-4f53-bdcc-22b145dd7c41"),
            WorkflowState: "PENDING_OPERATOR_REVIEW",
            CorrelationId,
            EnvironmentName: "Development",
            UsesLocalDevFallbackContext: false);

    private static OperatorConsoleAccessReadinessRepositoryResult MissingRepositoryCapabilities() =>
        new(
            new OperatorConsoleAccessReadinessRepositoryCapabilities(
                OperatorConsoleSchemaExists: false,
                HrIdentityMappingsTableExists: false,
                OperatorDeviceBindingsTableExists: false,
                OperatorDeviceAssignmentHistoryTableExists: false,
                OperatorShiftsTableExists: false,
                OperatorAccessEvaluationsTableExists: false,
                OperatorAccessEvaluationReasonsTableExists: false),
            OperatorDenialReasons: [],
            DeviceDenialReasons: [],
            ShiftDenialReasons: [],
            SiteDenialReasons: []);

    private static OperatorConsoleAccessReadinessRepositoryCapabilities FullRepositoryCapabilities() =>
        new(
            OperatorConsoleSchemaExists: true,
            HrIdentityMappingsTableExists: true,
            OperatorDeviceBindingsTableExists: true,
            OperatorDeviceAssignmentHistoryTableExists: true,
            OperatorShiftsTableExists: true,
            OperatorAccessEvaluationsTableExists: true,
            OperatorAccessEvaluationReasonsTableExists: true);
}
