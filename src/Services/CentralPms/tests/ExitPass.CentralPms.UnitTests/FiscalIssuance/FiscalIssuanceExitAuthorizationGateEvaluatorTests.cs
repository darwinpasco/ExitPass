using System.Net.Http;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceExitAuthorizationGateEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenRecordedEvidenceIsComplete_IsReady()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded));

        result.IsReadyForNormalExitAuthorization.Should().BeTrue();
        result.BlockedReason.Should().BeNull();
    }

    [Fact]
    public void Evaluate_WhenReplayedEvidenceIsComplete_IsReady()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed));

        result.IsReadyForNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenReconciledEvidenceIsCompleteAndPolicyApproved_IsReady()
    {
        var result = Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceReconciled),
            Context(isReconciledFiscalEvidencePolicyApproved: true));

        result.IsReadyForNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenReconciledPolicyIsNotApproved_Blocks()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceReconciled));

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_issuance_reconciled_policy_required");
    }

    [Fact]
    public void Evaluate_WhenNotRequiredPolicyIsApproved_IsReady()
    {
        var result = Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.NotRequired),
            Context(isNoFiscalRequiredPolicyApproved: true));

        result.IsReadyForNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenNotRequiredPolicyIsNotApproved_Blocks()
    {
        var result = Evaluate(MinimalReference(FiscalIssuanceIntegrationState.NotRequired));

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_issuance_not_required_policy_required");
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.PendingFiscalIssuance, "fiscal_issuance_pending", false)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRequested, "fiscal_issuance_requested", false)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceConflict, "fiscal_issuance_conflict", true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest, "fiscal_issuance_failed_request", true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration, "fiscal_issuance_failed_configuration", true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService, "fiscal_issuance_failed_service", true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown, "fiscal_issuance_unknown", true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceManualReview, "fiscal_issuance_manual_review", true)]
    public void Evaluate_WhenStateIsNotNormalExitReady_BlocksWithReason(
        FiscalIssuanceIntegrationState state,
        string blockedReason,
        bool requiresManualReview)
    {
        var result = Evaluate(MinimalReference(state));

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be(blockedReason);
        result.RequiresManualReview.Should().Be(requiresManualReview);
    }

    [Fact]
    public void Evaluate_WhenExceptionReleased_IsExceptionReleaseOnlyAndNotNormalReady()
    {
        var result = Evaluate(MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased));

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_issuance_exception_release_only");
        result.IsExceptionReleaseOnly.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenEvidenceStatusIsMissing_Blocks()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            FiscalIssuanceEvidenceStatus = null
        });

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_issuance_evidence_incomplete");
    }

    [Fact]
    public void Evaluate_WhenFiscalNumberIsNotAssigned_Blocks()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.NotAssigned
        });

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_number_not_assigned");
    }

    [Fact]
    public void Evaluate_WhenFiscalDocumentNumberIsMissing_Blocks()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            FiscalDocumentNumber = null
        });

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_issuance_evidence_incomplete");
    }

    [Fact]
    public void Evaluate_WhenPosServerFiscalDocumentIdIsMissing_BlocksAsReferenceNotRecorded()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            PosServerFiscalDocumentId = null
        });

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_reference_not_recorded");
    }

    [Fact]
    public void Evaluate_WhenFirstRecordedAtIsMissing_BlocksAsReferenceNotRecorded()
    {
        var result = Evaluate(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
        {
            FirstRecordedAt = default
        });

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("fiscal_reference_not_recorded");
    }

    [Fact]
    public void Evaluate_WhenPaymentFinalityIsNotVerified_Blocks()
    {
        var result = Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            Context(isPaymentFinalityVerified: false));

        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
        result.BlockedReason.Should().Be("payment_finality_not_verified");
    }

    [Fact]
    public void Evaluator_DoesNotIntroducePosServerNetworkDependencies()
    {
        var constructorParameters = typeof(FiscalIssuanceExitAuthorizationGateEvaluator)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().BeEmpty();
        constructorParameters.Should().NotContain(type =>
            type == typeof(HttpClient) ||
            type == typeof(IPosServerFiscalDocumentClient));
    }

    [Fact]
    public void GatingOptions_DefaultToEnforcementOffAndShadowOn()
    {
        var options = new FiscalIssuanceExitAuthorizationGatingOptions();

        options.EnableFiscalBeforeExitAuthorizationEnforcement.Should().BeFalse();
        options.EnableShadowEvaluation.Should().BeTrue();
        options.ReadinessMode.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessModes.ReadinessOnly);
    }

    [Fact]
    public void GatingOptions_WhenBoundFromEmptyConfiguration_DoNotRequireConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.Configure<FiscalIssuanceExitAuthorizationGatingOptions>(
            configuration.GetSection(FiscalIssuanceExitAuthorizationGatingOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<FiscalIssuanceExitAuthorizationGatingOptions>>()
            .Value;

        options.EnableFiscalBeforeExitAuthorizationEnforcement.Should().BeFalse();
        options.EnableShadowEvaluation.Should().BeTrue();
        options.ReadinessMode.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessModes.ReadinessOnly);
    }

    [Fact]
    public void GatingReadiness_WhenOptionsAreDefault_ReportsDefaultOffAndReadinessOnly()
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            Context());

        readiness.EnforcementConfigured.Should().BeFalse();
        readiness.EnforcementWiredForBlocking.Should().BeFalse();
        readiness.ShadowEvaluationEnabled.Should().BeTrue();
        readiness.ConfigurationStatus.Should().Be(
            FiscalIssuanceExitAuthorizationGatingConfigurationStatuses.EnforcementOffDefault);
        readiness.ReadinessStatus.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldAllow);
        readiness.WouldAllowNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void GatingReadiness_WhenFutureEnforcementFlagIsConfigured_RemainsReadinessOnly()
    {
        var options = new FiscalIssuanceExitAuthorizationGatingOptions
        {
            EnableFiscalBeforeExitAuthorizationEnforcement = true
        };

        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown),
            Context(),
            options);

        readiness.EnforcementConfigured.Should().BeTrue();
        readiness.EnforcementWiredForBlocking.Should().BeFalse();
        readiness.ConfigurationStatus.Should().Be(
            FiscalIssuanceExitAuthorizationGatingConfigurationStatuses.EnforcementConfiguredReadinessOnly);
        readiness.ReadinessStatus.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldBlock);
        readiness.BlockedReason.Should().Be("fiscal_issuance_unknown");
        readiness.WouldAllowNormalExitAuthorization.Should().BeFalse();
    }

    [Fact]
    public void GatingReadiness_WhenRecordedEvidenceIsComplete_ReportsWouldAllow()
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            Context());

        readiness.ReadinessStatus.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldAllow);
        readiness.WouldAllowNormalExitAuthorization.Should().BeTrue();
        readiness.BlockedReason.Should().BeNull();
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.PendingFiscalIssuance)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRequested)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceConflict)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceManualReview)]
    public void GatingReadiness_WhenFiscalStateWouldBlock_ReportsWouldBlock(
        FiscalIssuanceIntegrationState state)
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            MinimalReference(state),
            Context());

        readiness.ReadinessStatus.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldBlock);
        readiness.WouldAllowNormalExitAuthorization.Should().BeFalse();
        readiness.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void GatingReadiness_WhenNotRequiredPolicyIsApproved_ReportsNotRequiredPosture()
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.NotRequired),
            Context(isNoFiscalRequiredPolicyApproved: true));

        readiness.ReadinessStatus.Should().Be(
            FiscalIssuanceExitAuthorizationGatingReadinessStatuses.NotRequiredPolicyPosture);
        readiness.WouldAllowNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void GatingReadiness_WhenExceptionReleased_ReportsExceptionReleaseOnly()
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased),
            Context());

        readiness.ReadinessStatus.Should().Be(
            FiscalIssuanceExitAuthorizationGatingReadinessStatuses.ExceptionReleaseOnly);
        readiness.WouldAllowNormalExitAuthorization.Should().BeFalse();
        readiness.IsExceptionReleaseOnly.Should().BeTrue();
    }

    [Fact]
    public void GatingReadiness_WhenShadowEvaluationIsDisabled_ReportsShadowDisabledWithoutEnforcement()
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            Context(),
            new FiscalIssuanceExitAuthorizationGatingOptions
            {
                EnableShadowEvaluation = false
            });

        readiness.ReadinessStatus.Should().Be(
            FiscalIssuanceExitAuthorizationGatingReadinessStatuses.ShadowEvaluationDisabled);
        readiness.WouldAllowNormalExitAuthorization.Should().BeTrue();
        readiness.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void EnforcementDecision_WhenOptionsAreDefault_IsDisabledAndNotWiredForBlocking()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            Context());

        decision.EnforcementEnabled.Should().BeFalse();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow);
    }

    [Fact]
    public void EnforcementDecision_WhenFutureFlagIsConfigured_IsStillNotWiredForBlocking()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.PendingFiscalIssuance),
            Context(),
            new FiscalIssuanceExitAuthorizationGatingOptions
            {
                EnableFiscalBeforeExitAuthorizationEnforcement = true
            });

        decision.EnforcementEnabled.Should().BeTrue();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void EnforcementDecision_WhenRecordedEvidenceIsComplete_WouldAllow()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow);
        decision.WouldAllowNormalExitAuthorization.Should().BeTrue();
        decision.WouldBlockNormalExitAuthorization.Should().BeFalse();
    }

    [Fact]
    public void EnforcementDecision_WhenReplayedEvidenceIsComplete_WouldAllow()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed),
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow);
        decision.WouldAllowNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void EnforcementDecision_WhenReconciledEvidenceIsCompleteAndPolicyApproved_WouldAllow()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceReconciled),
            Context(isReconciledFiscalEvidencePolicyApproved: true));

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow);
        decision.WouldAllowNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void EnforcementDecision_WhenNotRequiredPolicyIsApproved_WouldAllowAsNotRequiredByPolicy()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.NotRequired),
            Context(isNoFiscalRequiredPolicyApproved: true));

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy);
        decision.WouldAllowNormalExitAuthorization.Should().BeTrue();
        decision.IsNotRequiredByPolicy.Should().BeTrue();
    }

    [Fact]
    public void EnforcementDecision_WhenNotRequiredPolicyIsMissing_WouldBlock()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.NotRequired),
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.BlockedReason.Should().Be("fiscal_issuance_not_required_policy_required");
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.PendingFiscalIssuance, "fiscal_issuance_pending", FiscalIssuanceExitAuthorizationEnforcementDecisions.Block)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRequested, "fiscal_issuance_requested", FiscalIssuanceExitAuthorizationEnforcementDecisions.Block)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceConflict, "fiscal_issuance_conflict", FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest, "fiscal_issuance_failed_request", FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration, "fiscal_issuance_failed_configuration", FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService, "fiscal_issuance_failed_service", FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown, "fiscal_issuance_unknown", FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceManualReview, "fiscal_issuance_manual_review", FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired)]
    public void EnforcementDecision_WhenStateIsNotEligible_WouldBlock(
        FiscalIssuanceIntegrationState state,
        string blockedReason,
        string expectedDecision)
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(state),
            Context());

        decision.Decision.Should().Be(expectedDecision);
        decision.WouldAllowNormalExitAuthorization.Should().BeFalse();
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.BlockedReason.Should().Be(blockedReason);
    }

    [Fact]
    public void EnforcementDecision_WhenExceptionReleased_IsExceptionReleaseOnly()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased),
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.ExceptionReleaseOnly);
        decision.WouldAllowNormalExitAuthorization.Should().BeFalse();
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.IsExceptionReleaseOnly.Should().BeTrue();
    }

    [Fact]
    public void EnforcementDecision_WhenFiscalEvidenceIsMissing_WouldBlock()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
            {
                FiscalIssuanceEvidenceStatus = null
            },
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        decision.BlockedReason.Should().Be("fiscal_issuance_evidence_incomplete");
    }

    [Fact]
    public void EnforcementDecision_WhenAssignmentStateIsNotAssigned_WouldBlock()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
            {
                FiscalNumberAssignmentState = FiscalNumberAssignmentState.NotAssigned
            },
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        decision.BlockedReason.Should().Be("fiscal_number_not_assigned");
    }

    [Fact]
    public void EnforcementDecision_WhenDurableFiscalReferenceIsMissing_WouldBlock()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
            {
                FirstRecordedAt = default
            },
            Context());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        decision.BlockedReason.Should().Be("fiscal_reference_not_recorded");
    }

    [Fact]
    public void EnforcementDecision_WhenReadinessCannotEvaluate_ReportsNotEvaluable()
    {
        var readiness = new FiscalIssuanceExitAuthorizationGatingReadiness(
            EnforcementConfigured: false,
            EnforcementWiredForBlocking: false,
            ShadowEvaluationEnabled: true,
            ReadinessMode: FiscalIssuanceExitAuthorizationGatingReadinessModes.ReadinessOnly,
            ConfigurationStatus: FiscalIssuanceExitAuthorizationGatingConfigurationStatuses.EnforcementOffDefault,
            ReadinessStatus: FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldBlock,
            WouldAllowNormalExitAuthorization: false,
            BlockedReason: null,
            State: null,
            RequiresManualReview: false,
            IsExceptionReleaseOnly: false);

        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.FromReadiness(readiness);

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.NotEvaluable);
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public void EnforcementPolicy_DoesNotIntroducePosServerNetworkDependencies()
    {
        var constructorParameters = typeof(FiscalIssuanceExitAuthorizationEnforcementPolicy)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().BeEmpty();
        constructorParameters.Should().NotContain(type =>
            type == typeof(HttpClient) ||
            type == typeof(IPosServerFiscalDocumentClient));
    }

    [Fact]
    public void IssueExitAuthorizationHandler_UsesOnlyShadowFiscalGatingAbstraction()
    {
        var constructorParameters = typeof(IssueExitAuthorizationHandler)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().Contain(typeof(IExitAuthorizationFiscalGatingShadowEvaluator));
        constructorParameters.Should().NotContain(typeof(IFiscalIssuanceReferenceRepository));
        constructorParameters.Should().NotContain(typeof(IPosServerFiscalDocumentClient));
        constructorParameters.Should().NotContain(typeof(FiscalIssuanceExitAuthorizationGatingOptions));
    }

    [Fact]
    public async Task ShadowEvaluator_WhenFiscalReferenceIsReady_ReturnsEvaluatedReady()
    {
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator();

        var result = await sut.EvaluateAsync(
            ShadowContext(CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded)),
            CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.EvaluatedReady);
        result.IsReadyForNormalExitAuthorization.Should().BeTrue();
    }

    [Fact]
    public async Task ShadowEvaluator_WhenFiscalReferenceIsBlocked_ReturnsEvaluatedBlockedReason()
    {
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator();

        var result = await sut.EvaluateAsync(
            ShadowContext(MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown)),
            CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.EvaluatedBlocked);
        result.BlockedReason.Should().Be("fiscal_issuance_unknown");
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task ShadowEvaluator_WhenFiscalReferenceIsMissing_ReturnsNotEvaluatedMissingContext()
    {
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator();

        var result = await sut.EvaluateAsync(ShadowContext(null), CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext);
        result.IsReadyForNormalExitAuthorization.Should().BeFalse();
    }

    [Fact]
    public async Task ShadowEvaluator_WhenRepositoryFindsReadyFiscalReference_ReturnsEvaluatedReady()
    {
        var reference = CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(reference.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(reference);
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator(repository);

        var result = await sut.EvaluateAsync(
            ShadowContext(null, reference.PaymentAttemptId),
            CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.EvaluatedReady);
        result.IsReadyForNormalExitAuthorization.Should().BeTrue();
        await repository.Received(1).FindLatestByPaymentAttemptIdAsync(
            reference.PaymentAttemptId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShadowEvaluator_WhenRepositoryFindsBlockedFiscalReference_ReturnsEvaluatedBlocked()
    {
        var reference = MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(reference.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(reference);
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator(repository);

        var result = await sut.EvaluateAsync(
            ShadowContext(null, reference.PaymentAttemptId),
            CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.EvaluatedBlocked);
        result.BlockedReason.Should().Be("fiscal_issuance_unknown");
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task ShadowEvaluator_WhenRepositoryDoesNotFindFiscalReference_ReturnsMissingContext()
    {
        var paymentAttemptId = Guid.NewGuid();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator(repository);

        var result = await sut.EvaluateAsync(
            ShadowContext(null, paymentAttemptId),
            CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext);
        await repository.Received(1).FindLatestByPaymentAttemptIdAsync(
            paymentAttemptId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShadowEvaluator_WhenContextAlreadyHasFiscalReference_DoesNotQueryRepository()
    {
        var reference = CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var sut = new ExitAuthorizationFiscalGatingShadowEvaluator(repository);

        var result = await sut.EvaluateAsync(
            ShadowContext(reference, reference.PaymentAttemptId),
            CancellationToken.None);

        result.Status.Should().Be(FiscalGatingShadowEvaluationStatuses.EvaluatedReady);
        _ = repository.DidNotReceiveWithAnyArgs().FindLatestByPaymentAttemptIdAsync(
            default,
            default);
    }

    private static FiscalIssuanceGatingEvaluation Evaluate(
        FiscalIssuanceReferenceRecord reference,
        FiscalIssuanceGatingEvaluationContext? context = null) =>
        FiscalIssuanceExitAuthorizationGateEvaluator.Evaluate(reference, context ?? Context());

    private static FiscalIssuanceGatingEvaluationContext Context(
        bool isPaymentFinalityVerified = true,
        bool isNoFiscalRequiredPolicyApproved = false,
        bool isReconciledFiscalEvidencePolicyApproved = false) =>
        new(
            IsPaymentFinalityVerified: isPaymentFinalityVerified,
            IsNoFiscalRequiredPolicyApproved: isNoFiscalRequiredPolicyApproved,
            IsReconciledFiscalEvidencePolicyApproved: isReconciledFiscalEvidencePolicyApproved);

    private static FiscalIssuanceReferenceRecord CompleteReference(
        FiscalIssuanceIntegrationState state) =>
        MinimalReference(state) with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 101,
            FiscalDocumentNumber = "SI-000101",
            FiscalSeries = "SI",
            FiscalNumberPrefixText = "SI-",
            FiscalNumberSuffixText = null,
            FiscalNumberAssignedAt = DateTimeOffset.Parse("2026-07-02T10:30:00+08:00"),
            FiscalNumberAssignedByRef = "pos-server",
            FiscalDocumentStatusCodeId = Guid.NewGuid(),
            ResultClassification = state == FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalIssuanceResultClassification.IdempotentReplay
                : FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned
        };

    private static FiscalIssuanceReferenceRecord MinimalReference(
        FiscalIssuanceIntegrationState state) =>
        new(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "site-pos-server-main",
            PayableBasisRef: "tariff-snapshot-ref",
            UpstreamFinalityReference: $"pay-final-{Guid.NewGuid():N}",
            PosServerFiscalDocumentId: null,
            FiscalIdentityId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-07-02T10:30:02+08:00"),
            RecordedByServiceIdentityId: Guid.NewGuid());

    private static ExitAuthorizationFiscalGatingShadowContext ShadowContext(
        FiscalIssuanceReferenceRecord? reference,
        Guid? paymentAttemptId = null) =>
        new(
            ParkingSessionId: Guid.NewGuid(),
            PaymentAttemptId: paymentAttemptId ?? Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            IsPaymentFinalityVerified: true,
            FiscalReference: reference);
}
