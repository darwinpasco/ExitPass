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

    public static FiscalIssuanceExitAuthorizationEnforcementDecision FromReadiness(
        FiscalIssuanceExitAuthorizationGatingReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var decision = ResolveDecision(readiness);
        var wouldBlock = decision is not
            FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow and not
            FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy;

        return new FiscalIssuanceExitAuthorizationEnforcementDecision(
            WouldAllowNormalExitAuthorization: !wouldBlock,
            WouldBlockNormalExitAuthorization: wouldBlock,
            Decision: decision,
            BlockedReason: readiness.BlockedReason,
            RequiresManualReview: readiness.RequiresManualReview,
            IsExceptionReleaseOnly: readiness.IsExceptionReleaseOnly,
            IsNotRequiredByPolicy: decision == FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy,
            EvidenceState: readiness.State,
            EnforcementEnabled: readiness.EnforcementConfigured,
            EnforcementWiredForBlocking: readiness.EnforcementWiredForBlocking);
    }

    private static string ResolveDecision(FiscalIssuanceExitAuthorizationGatingReadiness readiness)
    {
        if (readiness.IsExceptionReleaseOnly)
        {
            return FiscalIssuanceExitAuthorizationEnforcementDecisions.ExceptionReleaseOnly;
        }

        if (readiness.ReadinessStatus ==
            FiscalIssuanceExitAuthorizationGatingReadinessStatuses.NotRequiredPolicyPosture)
        {
            return readiness.WouldAllowNormalExitAuthorization
                ? FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy
                : FiscalIssuanceExitAuthorizationEnforcementDecisions.Block;
        }

        if (readiness.RequiresManualReview)
        {
            return FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired;
        }

        if (readiness.WouldAllowNormalExitAuthorization)
        {
            return FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow;
        }

        return string.IsNullOrWhiteSpace(readiness.BlockedReason)
            ? FiscalIssuanceExitAuthorizationEnforcementDecisions.NotEvaluable
            : FiscalIssuanceExitAuthorizationEnforcementDecisions.Block;
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
