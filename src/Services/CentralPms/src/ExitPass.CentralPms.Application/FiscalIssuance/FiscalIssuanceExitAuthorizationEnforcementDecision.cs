using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public static class FiscalIssuanceExitAuthorizationEnforcementPolicy
{
    public static FiscalIssuanceExitAuthorizationEnforcementDecision Evaluate(
        FiscalIssuanceReferenceRecord? reference,
        FiscalIssuanceGatingEvaluationContext context,
        FiscalIssuanceExitAuthorizationGatingOptions? options = null)
    {
        var readiness = FiscalIssuanceExitAuthorizationGatingReadinessEvaluator.Evaluate(
            reference,
            context,
            options);

        return FromReadiness(readiness);
    }

    public static FiscalIssuanceExitAuthorizationEnforcementDecision FromShadowEvaluation(
        FiscalGatingShadowEvaluation evaluation,
        FiscalIssuanceExitAuthorizationGatingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var effectiveOptions = options ?? new FiscalIssuanceExitAuthorizationGatingOptions();

        if (evaluation.Status == FiscalGatingShadowEvaluationStatuses.NotEvaluatedNotRequired)
        {
            return Create(
                decision: FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy,
                blockedReason: null,
                requiresManualReview: false,
                isExceptionReleaseOnly: false,
                isNotRequiredByPolicy: true,
                evidenceState: evaluation.State,
                options: effectiveOptions);
        }

        if (evaluation.Status is
            FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext or
            FiscalGatingShadowEvaluationStatuses.EvaluationFailedNonBlocking)
        {
            return Create(
                decision: FiscalIssuanceExitAuthorizationEnforcementDecisions.NotEvaluable,
                blockedReason: evaluation.BlockedReason,
                requiresManualReview: evaluation.RequiresManualReview,
                isExceptionReleaseOnly: evaluation.IsExceptionReleaseOnly,
                isNotRequiredByPolicy: false,
                evidenceState: evaluation.State,
                options: effectiveOptions);
        }

        return Create(
            decision: ResolveDecision(
                evaluation.IsReadyForNormalExitAuthorization,
                evaluation.BlockedReason,
                evaluation.State,
                evaluation.RequiresManualReview,
                evaluation.IsExceptionReleaseOnly,
                isNotRequiredByPolicyPosture: false),
            blockedReason: evaluation.BlockedReason,
            requiresManualReview: evaluation.RequiresManualReview,
            isExceptionReleaseOnly: evaluation.IsExceptionReleaseOnly,
            isNotRequiredByPolicy: false,
            evidenceState: evaluation.State,
            options: effectiveOptions);
    }

    public static FiscalIssuanceExitAuthorizationEnforcementDecision FromReadiness(
        FiscalIssuanceExitAuthorizationGatingReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var decision = ResolveDecision(readiness);

        return Create(
            decision,
            readiness.BlockedReason,
            readiness.RequiresManualReview,
            readiness.IsExceptionReleaseOnly,
            decision == FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy,
            readiness.State,
            readiness.EnforcementConfigured,
            readiness.EnforcementWiredForBlocking);
    }

    private static string ResolveDecision(FiscalIssuanceExitAuthorizationGatingReadiness readiness)
    {
        var isNotRequiredByPolicyPosture = readiness.ReadinessStatus ==
            FiscalIssuanceExitAuthorizationGatingReadinessStatuses.NotRequiredPolicyPosture;

        return ResolveDecision(
            readiness.WouldAllowNormalExitAuthorization,
            readiness.BlockedReason,
            readiness.State,
            readiness.RequiresManualReview,
            readiness.IsExceptionReleaseOnly,
            isNotRequiredByPolicyPosture);
    }

    private static string ResolveDecision(
        bool wouldAllowNormalExitAuthorization,
        string? blockedReason,
        FiscalIssuanceIntegrationState? evidenceState,
        bool requiresManualReview,
        bool isExceptionReleaseOnly,
        bool isNotRequiredByPolicyPosture)
    {
        if (isExceptionReleaseOnly)
        {
            return FiscalIssuanceExitAuthorizationEnforcementDecisions.ExceptionReleaseOnly;
        }

        if (isNotRequiredByPolicyPosture)
        {
            return wouldAllowNormalExitAuthorization
                ? FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy
                : FiscalIssuanceExitAuthorizationEnforcementDecisions.Block;
        }

        if (requiresManualReview &&
            evidenceState == FiscalIssuanceIntegrationState.FiscalIssuanceManualReview)
        {
            return FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired;
        }

        if (wouldAllowNormalExitAuthorization)
        {
            return FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow;
        }

        return string.IsNullOrWhiteSpace(blockedReason)
            ? FiscalIssuanceExitAuthorizationEnforcementDecisions.NotEvaluable
            : FiscalIssuanceExitAuthorizationEnforcementDecisions.Block;
    }

    private static FiscalIssuanceExitAuthorizationEnforcementDecision Create(
        string decision,
        string? blockedReason,
        bool requiresManualReview,
        bool isExceptionReleaseOnly,
        bool isNotRequiredByPolicy,
        FiscalIssuanceIntegrationState? evidenceState,
        FiscalIssuanceExitAuthorizationGatingOptions options) =>
        Create(
            decision,
            blockedReason,
            requiresManualReview,
            isExceptionReleaseOnly,
            isNotRequiredByPolicy,
            evidenceState,
            options.EnableFiscalBeforeExitAuthorizationEnforcement,
            enforcementWiredForBlocking: false);

    private static FiscalIssuanceExitAuthorizationEnforcementDecision Create(
        string decision,
        string? blockedReason,
        bool requiresManualReview,
        bool isExceptionReleaseOnly,
        bool isNotRequiredByPolicy,
        FiscalIssuanceIntegrationState? evidenceState,
        bool enforcementEnabled,
        bool enforcementWiredForBlocking)
    {
        var wouldBlock = decision is not
            FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow and not
            FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy;

        return new FiscalIssuanceExitAuthorizationEnforcementDecision(
            WouldAllowNormalExitAuthorization: !wouldBlock,
            WouldBlockNormalExitAuthorization: wouldBlock,
            Decision: decision,
            BlockedReason: blockedReason,
            RequiresManualReview: requiresManualReview,
            IsExceptionReleaseOnly: isExceptionReleaseOnly,
            IsNotRequiredByPolicy: isNotRequiredByPolicy,
            EvidenceState: evidenceState,
            EnforcementEnabled: enforcementEnabled,
            EnforcementWiredForBlocking: enforcementWiredForBlocking);
    }
}

public sealed record FiscalIssuanceExitAuthorizationEnforcementDecision(
    bool WouldAllowNormalExitAuthorization,
    bool WouldBlockNormalExitAuthorization,
    string Decision,
    string? BlockedReason,
    bool RequiresManualReview,
    bool IsExceptionReleaseOnly,
    bool IsNotRequiredByPolicy,
    FiscalIssuanceIntegrationState? EvidenceState,
    bool EnforcementEnabled,
    bool EnforcementWiredForBlocking);

public static class FiscalIssuanceExitAuthorizationEnforcementDecisions
{
    public const string Allow = "allow";
    public const string Block = "block";
    public const string NotRequiredByPolicy = "not_required_by_policy";
    public const string ExceptionReleaseOnly = "exception_release_only";
    public const string ManualReviewRequired = "manual_review_required";
    public const string NotEvaluable = "not_evaluable";
}
