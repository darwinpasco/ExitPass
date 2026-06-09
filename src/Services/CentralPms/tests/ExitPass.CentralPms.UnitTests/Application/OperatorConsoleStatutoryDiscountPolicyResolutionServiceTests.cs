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
    /// Verifies production policy resolution does not auto-resolve sandbox fixture policy rows.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenProductionAndSandboxPolicyResolved_ReturnsManualReviewFailClosed()
    {
        var repository = RepositoryReturning(Policy(
            policyId: Guid.Parse("23100000-0000-0000-0000-000000000002"),
            policyCode: "SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A",
            policyName: "Sandbox Operator Console Senior Citizen Required Evidence Policy",
            policyLevel: "SITE_POLICY",
            policyType: "SITE_POLICY",
            policyResolutionBasis: "SITE_POLICY_OPERATIONAL_ONLY",
            ordinanceReference: "SANDBOX-OC-SD-ORD-235A",
            nationalLawReference: null,
            sourceReference: "SANDBOX_METADATA_ONLY"));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeFalse();
        result.Policy.Should().BeNull();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        result.RequiresManualReview.Should().BeTrue();
        result.PolicyReadinessReason.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        result.OperatorMessage.Should().Contain("sandbox");
    }

    /// <summary>
    /// Verifies non-production sandbox validation remains supported.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenDevelopmentAndSandboxPolicyResolved_ReturnsPolicy()
    {
        var repository = RepositoryReturning(Policy(
            policyId: Guid.Parse("23100000-0000-0000-0000-000000000002"),
            policyCode: "SANDBOX_OC_SD_REQUIRED_EVIDENCE_POLICY_235A",
            policyName: "Sandbox Operator Console Senior Citizen Required Evidence Policy",
            policyLevel: "SITE_POLICY",
            policyType: "SITE_POLICY",
            policyResolutionBasis: "SITE_POLICY_OPERATIONAL_ONLY",
            ordinanceReference: "SANDBOX-OC-SD-ORD-235A",
            nationalLawReference: null,
            sourceReference: "SANDBOX_METADATA_ONLY"));
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeTrue();
        result.Policy.Should().NotBeNull();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.SandboxOnly);
        result.RequiresManualReview.Should().BeFalse();
    }

    /// <summary>
    /// Verifies missing production policies return a controlled not-ready result.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenProductionAndPolicyMissing_ReturnsMissingRequiredPolicy()
    {
        var repository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        repository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: false,
                Policy: null,
                SiteId,
                SiteGroupId,
                JurisdictionId,
                "NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED",
                "NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED"));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeFalse();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.MissingRequiredPolicy);
        result.RequiresManualReview.Should().BeTrue();
        result.ErrorCode.Should().Be("NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED");
    }

    /// <summary>
    /// Verifies inactive or expired rows are not production-ready.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenProductionAndPolicyExpired_ReturnsExpiredOrInactive()
    {
        var repository = RepositoryReturning(Policy(effectiveTo: new DateOnly(2026, 5, 1)));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeFalse();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ExpiredOrInactive);
        result.RequiresManualReview.Should().BeTrue();
    }

    /// <summary>
    /// Verifies missing evidence requirements are not production-ready for the Operator Console workflow.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenProductionAndPolicyMissingEvidenceRule_ReturnsMissingEvidenceRule()
    {
        var repository = RepositoryReturning(Policy(requiresEvidence: false));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeFalse();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.MissingEvidenceRule);
        result.RequiresManualReview.Should().BeTrue();
    }

    /// <summary>
    /// Verifies dedicated-registry active-approved rows classify as production verified when required fields are present.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenProductionAndDedicatedRegistryPolicyActiveApproved_ReturnsReadyVerified()
    {
        var repository = RepositoryReturning(Policy(verificationStatus: "ACTIVE_APPROVED", sourceReference: "DB repo approved registry baseline"));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeTrue();
        result.Policy.Should().NotBeNull();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyVerified);
        result.RequiresManualReview.Should().BeFalse();
    }

    /// <summary>
    /// Verifies pilot-approved dedicated-registry rows stay manual-review only.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenProductionAndDedicatedRegistryPolicyPilotApproved_ReturnsManualReview()
    {
        var repository = RepositoryReturning(Policy(verificationStatus: "APPROVED_FOR_PILOT", sourceReference: "Pilot-approved registry row"));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeTrue();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    /// <summary>
    /// Verifies unverified/proposed dedicated-registry rows are not production-ready.
    /// </summary>
    [Theory]
    [InlineData("LEAD_UNVERIFIED")]
    [InlineData("VERIFIED_SECONDARY")]
    [InlineData("PROPOSED_ONLY")]
    public async Task ResolveAsync_WhenProductionAndDedicatedRegistryPolicyNotVerified_ReturnsConfiguredButUnverified(
        string verificationStatus)
    {
        var repository = RepositoryReturning(Policy(verificationStatus: verificationStatus, sourceReference: "Registry lead row"));
        var sut = CreateSut(AccessResult(allowed: true, []), repository, environmentName: "Production");

        var result = await sut.ResolveAsync(Command(), CancellationToken.None);

        result.PolicyResolved.Should().BeFalse();
        result.Policy.Should().BeNull();
        result.PolicyReadinessClassification.Should().Be(OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified);
        result.RequiresManualReview.Should().BeTrue();
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
        IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository? repository = null,
        string environmentName = "Development")
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
            clock,
            new OperatorConsolePolicyReadinessEnvironment(environmentName));
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

    private static IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository RepositoryReturning(
        OperatorConsoleResolvedStatutoryDiscountPolicy policy)
    {
        var repository = Substitute.For<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>();
        repository.ResolveAsync(Arg.Any<OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
                Resolved: true,
                policy,
                SiteId,
                SiteGroupId,
                JurisdictionId,
                IneligibilityReason: null,
                ErrorCode: null));
        return repository;
    }

    private static OperatorConsoleResolvedStatutoryDiscountPolicy Policy(
        Guid? policyId = null,
        string policyCode = "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
        string policyName = "RA 9994 Senior Citizen National Fallback",
        string policyResolutionBasis = "NATIONAL_LAW_FALLBACK",
        string policyLevel = "NATIONAL_LAW",
        string policyType = "LEGAL_REFERENCE",
        string? legalBasisReference = "Expanded Senior Citizens Act of 2010",
        string? ordinanceReference = null,
        string? nationalLawReference = "RA 9994",
        string verificationStatus = "VERIFIED_OFFICIAL",
        bool requiresEvidence = true,
        DateOnly? effectiveTo = null,
        string? sourceReference = null) =>
        new(
            policyId ?? PolicyId,
            JurisdictionId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            policyCode,
            policyName,
            policyResolutionBasis,
            policyLevel,
            policyType,
            legalBasisReference,
            ordinanceReference,
            nationalLawReference,
            verificationStatus,
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
            RequiresEvidence: requiresEvidence,
            new DateOnly(2026, 1, 1),
            effectiveTo,
            sourceReference,
            JsonSerializer.SerializeToElement(new { policyCode }));

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
