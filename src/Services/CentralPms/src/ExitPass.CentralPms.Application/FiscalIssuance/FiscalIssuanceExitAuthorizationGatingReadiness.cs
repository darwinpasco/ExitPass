using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalIssuanceExitAuthorizationGatingOptions
{
    public const string SectionName = "FiscalIssuance:ExitAuthorizationGating";

    public bool EnableFiscalBeforeExitAuthorizationEnforcement { get; set; } = true;

    public bool EnableShadowEvaluation { get; set; } = true;

    public string ReadinessMode { get; set; } = FiscalIssuanceExitAuthorizationGatingReadinessModes.HardBlocking;
}

public static class FiscalIssuanceExitAuthorizationGatingReadinessModes
{
    public const string ReadinessOnly = "readiness_only";
    public const string HardBlocking = "hard_blocking";
}

public static class FiscalIssuanceExitAuthorizationGatingReadinessEvaluator
{
    public static FiscalIssuanceExitAuthorizationGatingReadiness Evaluate(
        FiscalIssuanceReferenceRecord? reference,
        FiscalIssuanceGatingEvaluationContext context,
        FiscalIssuanceExitAuthorizationGatingOptions? options = null)
    {
        var effectiveOptions = options ?? new FiscalIssuanceExitAuthorizationGatingOptions();
        var evaluation = FiscalIssuanceExitAuthorizationGateEvaluator.Evaluate(reference, context);

        return new FiscalIssuanceExitAuthorizationGatingReadiness(
            EnforcementConfigured: effectiveOptions.EnableFiscalBeforeExitAuthorizationEnforcement,
            EnforcementWiredForBlocking: effectiveOptions.EnableFiscalBeforeExitAuthorizationEnforcement,
            ShadowEvaluationEnabled: effectiveOptions.EnableShadowEvaluation,
            ReadinessMode: string.IsNullOrWhiteSpace(effectiveOptions.ReadinessMode)
                ? FiscalIssuanceExitAuthorizationGatingReadinessModes.ReadinessOnly
                : effectiveOptions.ReadinessMode,
            ConfigurationStatus: ResolveConfigurationStatus(effectiveOptions),
            ReadinessStatus: ResolveReadinessStatus(evaluation, reference, effectiveOptions),
            WouldAllowNormalExitAuthorization: evaluation.IsReadyForNormalExitAuthorization,
            BlockedReason: evaluation.BlockedReason,
            State: evaluation.State,
            RequiresManualReview: evaluation.RequiresManualReview,
            IsExceptionReleaseOnly: evaluation.IsExceptionReleaseOnly);
    }

    private static string ResolveConfigurationStatus(FiscalIssuanceExitAuthorizationGatingOptions options) =>
        options.EnableFiscalBeforeExitAuthorizationEnforcement
            ? FiscalIssuanceExitAuthorizationGatingConfigurationStatuses.EnforcementConfiguredHardBlocking
            : FiscalIssuanceExitAuthorizationGatingConfigurationStatuses.EnforcementOffDefault;

    private static string ResolveReadinessStatus(
        FiscalIssuanceGatingEvaluation evaluation,
        FiscalIssuanceReferenceRecord? reference,
        FiscalIssuanceExitAuthorizationGatingOptions options)
    {
        if (!options.EnableShadowEvaluation)
        {
            return FiscalIssuanceExitAuthorizationGatingReadinessStatuses.ShadowEvaluationDisabled;
        }

        if (evaluation.IsExceptionReleaseOnly)
        {
            return FiscalIssuanceExitAuthorizationGatingReadinessStatuses.ExceptionReleaseOnly;
        }

        if (reference?.FiscalIssuanceState == FiscalIssuanceIntegrationState.NotRequired)
        {
            return FiscalIssuanceExitAuthorizationGatingReadinessStatuses.NotRequiredPolicyPosture;
        }

        return evaluation.IsReadyForNormalExitAuthorization
            ? FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldAllow
            : FiscalIssuanceExitAuthorizationGatingReadinessStatuses.WouldBlock;
    }
}

public sealed record FiscalIssuanceExitAuthorizationGatingReadiness(
    bool EnforcementConfigured,
    bool EnforcementWiredForBlocking,
    bool ShadowEvaluationEnabled,
    string ReadinessMode,
    string ConfigurationStatus,
    string ReadinessStatus,
    bool WouldAllowNormalExitAuthorization,
    string? BlockedReason,
    FiscalIssuanceIntegrationState? State,
    bool RequiresManualReview,
    bool IsExceptionReleaseOnly);

public static class FiscalIssuanceExitAuthorizationGatingConfigurationStatuses
{
    public const string EnforcementOffDefault = "enforcement_off_default";
    public const string EnforcementConfiguredReadinessOnly = "enforcement_configured_readiness_only";
    public const string EnforcementConfiguredHardBlocking = "enforcement_configured_hard_blocking";
}

public static class FiscalIssuanceExitAuthorizationGatingReadinessStatuses
{
    public const string WouldAllow = "would_allow";
    public const string WouldBlock = "would_block";
    public const string NotRequiredPolicyPosture = "not_required_policy_posture";
    public const string ExceptionReleaseOnly = "exception_release_only";
    public const string ShadowEvaluationDisabled = "shadow_evaluation_disabled";
}
