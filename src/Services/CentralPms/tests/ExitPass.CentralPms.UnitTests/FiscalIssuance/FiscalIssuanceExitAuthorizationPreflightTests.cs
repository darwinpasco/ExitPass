using System.Net.Http;
using System.Text.Json;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceExitAuthorizationPreflightTests
{
    [Fact]
    public void GatingOptions_Defaults_PrepareReadinessOnlyShadowMode()
    {
        var options = new FiscalIssuanceExitAuthorizationGatingOptions();
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            CompleteReference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            DefaultContext(),
            options);

        options.EnableFiscalBeforeExitAuthorizationEnforcement.Should().BeFalse();
        options.EnableShadowEvaluation.Should().BeTrue();
        options.ReadinessMode.Should().Be(FiscalIssuanceExitAuthorizationGatingReadinessModes.ReadinessOnly);
        readiness.EnforcementConfigured.Should().BeFalse();
        readiness.EnforcementWiredForBlocking.Should().BeFalse();
        readiness.ConfigurationStatus.Should().Be(
            FiscalIssuanceExitAuthorizationGatingConfigurationStatuses.EnforcementOffDefault);
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed)]
    public void EnforcementDecision_WhenCompleteRecordedOrReplayedEvidence_WouldAllowButDoesNotWireBlocking(
        FiscalIssuanceIntegrationState state)
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            CompleteReference(state),
            DefaultContext());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow);
        decision.WouldAllowNormalExitAuthorization.Should().BeTrue();
        decision.WouldBlockNormalExitAuthorization.Should().BeFalse();
        decision.EnforcementEnabled.Should().BeFalse();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.PendingFiscalIssuance, "fiscal_issuance_pending")]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRequested, "fiscal_issuance_requested")]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceConflict, "fiscal_issuance_conflict")]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest, "fiscal_issuance_failed_request")]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration, "fiscal_issuance_failed_configuration")]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService, "fiscal_issuance_failed_service")]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown, "fiscal_issuance_unknown")]
    public void EnforcementDecision_WhenFiscalStateIsNotReady_WouldBlockButDoesNotWireBlocking(
        FiscalIssuanceIntegrationState state,
        string blockedReason)
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(state),
            DefaultContext());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        decision.BlockedReason.Should().Be(blockedReason);
        decision.WouldAllowNormalExitAuthorization.Should().BeFalse();
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.EnforcementEnabled.Should().BeFalse();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void EnforcementDecision_WhenNotRequiredPolicyIsApproved_ReportsNotRequiredByPolicy()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.NotRequired),
            DefaultContext(isNoFiscalRequiredPolicyApproved: true));

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy);
        decision.IsNotRequiredByPolicy.Should().BeTrue();
        decision.WouldAllowNormalExitAuthorization.Should().BeTrue();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void EnforcementDecision_WhenExceptionReleased_ReportsExceptionReleaseOnly()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased),
            DefaultContext());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.ExceptionReleaseOnly);
        decision.IsExceptionReleaseOnly.Should().BeTrue();
        decision.WouldAllowNormalExitAuthorization.Should().BeFalse();
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void EnforcementDecision_WhenManualReviewRequired_ReportsManualReviewRequired()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.Evaluate(
            MinimalReference(FiscalIssuanceIntegrationState.FiscalIssuanceManualReview),
            DefaultContext());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired);
        decision.RequiresManualReview.Should().BeTrue();
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void EnforcementDecision_WhenFiscalContextIsMissing_ReportsNotEvaluable()
    {
        var decision = FiscalIssuanceExitAuthorizationEnforcementPolicy.FromShadowEvaluation(
            FiscalGatingShadowEvaluation.NotEvaluatedMissingFiscalContext());

        decision.Decision.Should().Be(FiscalIssuanceExitAuthorizationEnforcementDecisions.NotEvaluable);
        decision.WouldBlockNormalExitAuthorization.Should().BeTrue();
        decision.EnforcementEnabled.Should().BeFalse();
        decision.EnforcementWiredForBlocking.Should().BeFalse();
    }

    [Fact]
    public void ShadowObservedPayload_WhenSerialized_IncludesPreflightDecisionFieldsAndSafeContextOnly()
    {
        var payload = new ExitAuthorizationFiscalGatingShadowObservedPayload
        {
            ParkingSessionId = Guid.NewGuid(),
            PaymentAttemptId = Guid.NewGuid(),
            PaymentConfirmationId = Guid.NewGuid(),
            FiscalIssuanceReferenceId = Guid.NewGuid(),
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalDocumentNumber = "SI-000001",
            FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceRecorded.ToString(),
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned.ToString(),
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned.ToString(),
            ShadowEvaluationStatus = FiscalGatingShadowEvaluationStatuses.EvaluatedReady,
            EnforcementDecision = FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow,
            WouldAllowNormalExitAuthorization = true,
            WouldBlockNormalExitAuthorization = false,
            EnforcementEnabled = false,
            EnforcementWiredForBlocking = false,
            SiteId = Guid.NewGuid(),
            SitePosServerId = Guid.NewGuid(),
            SitePosServerRef = "site-pos-server-001",
            CorrelationId = Guid.NewGuid(),
            Source = "IssueExitAuthorizationHandler",
            ObservedAtUtc = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(payload);
        var propertyNames = typeof(ExitAuthorizationFiscalGatingShadowObservedPayload)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        json.Should().Contain(nameof(ExitAuthorizationFiscalGatingShadowObservedPayload.EnforcementDecision));
        json.Should().Contain(nameof(ExitAuthorizationFiscalGatingShadowObservedPayload.WouldAllowNormalExitAuthorization));
        json.Should().Contain(nameof(ExitAuthorizationFiscalGatingShadowObservedPayload.EnforcementEnabled));
        json.Should().Contain(nameof(ExitAuthorizationFiscalGatingShadowObservedPayload.EnforcementWiredForBlocking));
        propertyNames.Should().NotContain(name => ContainsSensitiveRawPayloadTerm(name));
    }

    [Fact]
    public void PreflightSurface_DoesNotDependOnPosServerNetworkOrBackgroundWorkers()
    {
        var constructorParameterTypes = new[]
            {
                typeof(ExitAuthorizationFiscalGatingShadowEvaluator),
                typeof(FiscalIssuanceExitAuthorizationGateEvaluator),
                typeof(FiscalIssuanceExitAuthorizationGatingReadinessEvaluator),
                typeof(FiscalIssuanceExitAuthorizationEnforcementPolicy)
            }
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameterTypes.Should().NotContain(type => type == typeof(HttpClient));
        constructorParameterTypes.Should().NotContain(type => type == typeof(IPosServerFiscalDocumentClient));
        constructorParameterTypes.Should().NotContain(type =>
            type.Name.Contains("HostedService", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("BackgroundService", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Scheduler", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("ReadbackWorker", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalIssuanceGatingEvaluationContext DefaultContext(
        bool isNoFiscalRequiredPolicyApproved = false) =>
        new(
            IsPaymentFinalityVerified: true,
            IsNoFiscalRequiredPolicyApproved: isNoFiscalRequiredPolicyApproved,
            IsReconciledFiscalEvidencePolicyApproved: true);

    private static FiscalIssuanceReferenceRecord MinimalReference(FiscalIssuanceIntegrationState state)
    {
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

        return new FiscalIssuanceReferenceRecord(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "site-pos-server-001",
            PayableBasisRef: "payable-basis-001",
            UpstreamFinalityReference: "upstream-finality-001",
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
            LatestExceptionReason: ExceptionReasonFor(state),
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid());
    }

    private static FiscalIssuanceReferenceRecord CompleteReference(FiscalIssuanceIntegrationState state)
    {
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

        return MinimalReference(state) with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 1001,
            FiscalDocumentNumber = "SI-000001",
            FiscalSeries = "SI",
            FiscalNumberPrefixText = "SI-",
            FiscalNumberSuffixText = null,
            FiscalNumberAssignedAt = now,
            FiscalNumberAssignedByRef = "pos-server-sequence",
            FiscalDocumentStatusCodeId = Guid.NewGuid(),
            ResultClassification = state == FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalIssuanceResultClassification.IdempotentReplay
                : FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned,
            PosServerResponseTimestamp = now
        };
    }

    private static FiscalIssuanceExceptionReason? ExceptionReasonFor(FiscalIssuanceIntegrationState state) =>
        state switch
        {
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict =>
                FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest =>
                FiscalIssuanceExceptionReason.RequestConstructionError,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration =>
                FiscalIssuanceExceptionReason.FiscalIdentityNotFound,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService =>
                FiscalIssuanceExceptionReason.PersistenceWriteFailed,
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown =>
                FiscalIssuanceExceptionReason.PostTimeout,
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview =>
                FiscalIssuanceExceptionReason.ManualReviewRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased =>
                FiscalIssuanceExceptionReason.ManualReleaseRequestedAfterFiscalFailure,
            _ => null
        };

    private static bool ContainsSensitiveRawPayloadTerm(string propertyName)
    {
        string[] bannedTerms =
        [
            "Raw",
            "Payload",
            "Pan",
            "Cvv",
            "Token",
            "Secret",
            "Credential",
            "EntitlementEvidence",
            "EvidenceImage"
        ];

        return bannedTerms.Any(term =>
            propertyName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
