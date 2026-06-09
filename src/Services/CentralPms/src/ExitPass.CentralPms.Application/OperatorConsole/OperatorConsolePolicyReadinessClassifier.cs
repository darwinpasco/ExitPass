namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Classifies statutory discount policy readiness without mutating policy state.
/// </summary>
public static class OperatorConsolePolicyReadinessClassifier
{
    private static readonly Guid Sandbox235APolicyId = Guid.Parse("23100000-0000-0000-0000-000000000002");

    /// <summary>
    /// Evaluates a policy resolution result for production auto-resolution readiness.
    /// </summary>
    public static OperatorConsolePolicyReadinessEvaluation Evaluate(
        OperatorConsoleStatutoryDiscountPolicyResolutionReadResult readResult,
        OperatorConsolePolicyReadinessEnvironment environment,
        DateOnly effectiveDate,
        bool evidenceRequiredByWorkflow)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(environment);

        var production = IsProduction(environment.EnvironmentName);
        if (!readResult.Resolved || readResult.Policy is null)
        {
            var classification = ClassifyMissing(readResult.ErrorCode, readResult.IneligibilityReason);
            return Build(
                classification,
                production,
                policyResolved: false,
                policy: null,
                canCreateDraft: false,
                readResult.IneligibilityReason ?? classification,
                readResult.ErrorCode ?? classification);
        }

        var policy = readResult.Policy;
        var resolvedClassification = ClassifyResolvedPolicy(policy, effectiveDate, evidenceRequiredByWorkflow);
        var productionResolvable = !production || resolvedClassification is
            OperatorConsolePolicyReadinessClassifications.ReadyVerified or
            OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview;
        var canCreateDraft = !production || resolvedClassification is
            OperatorConsolePolicyReadinessClassifications.ReadyVerified or
            OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview;

        return Build(
            resolvedClassification,
            production,
            productionResolvable,
            productionResolvable ? policy : null,
            canCreateDraft,
            resolvedClassification,
            resolvedClassification);
    }

    private static string ClassifyMissing(string? errorCode, string? ineligibilityReason)
    {
        var reason = Normalize(errorCode) ?? Normalize(ineligibilityReason);
        return reason switch
        {
            "SITE_JURISDICTION_NOT_CONFIGURED" or "SITE_GROUP_MISMATCH" or "SITE_NOT_FOUND" =>
                OperatorConsolePolicyReadinessClassifications.MissingSiteMapping,
            "NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED" or "STATUTORY_DISCOUNT_POLICY_NOT_RESOLVED" =>
                OperatorConsolePolicyReadinessClassifications.MissingRequiredPolicy,
            "STATUTORY_DISCOUNT_POLICY_UNVERIFIED" =>
                OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified,
            _ => OperatorConsolePolicyReadinessClassifications.NotReady
        };
    }

    private static string ClassifyResolvedPolicy(
        OperatorConsoleResolvedStatutoryDiscountPolicy policy,
        DateOnly effectiveDate,
        bool evidenceRequiredByWorkflow)
    {
        if (IsSandboxOrDevelopmentOnly(policy))
        {
            return OperatorConsolePolicyReadinessClassifications.SandboxOnly;
        }

        var verificationReadiness = ClassifyVerificationStatus(policy.VerificationStatus);
        if (verificationReadiness == VerificationReadiness.Inactive)
        {
            return OperatorConsolePolicyReadinessClassifications.ExpiredOrInactive;
        }

        if (policy.EffectiveFrom > effectiveDate ||
            (policy.EffectiveTo.HasValue && policy.EffectiveTo.Value < effectiveDate))
        {
            return OperatorConsolePolicyReadinessClassifications.ExpiredOrInactive;
        }

        if (!policy.RequiresOperatorValidation)
        {
            return OperatorConsolePolicyReadinessClassifications.NotReady;
        }

        if (evidenceRequiredByWorkflow && !policy.RequiresEvidence)
        {
            return OperatorConsolePolicyReadinessClassifications.MissingEvidenceRule;
        }

        if (RequiresScopedMapping(policy) && (policy.SiteId == Guid.Empty || policy.SiteGroupId == Guid.Empty))
        {
            return OperatorConsolePolicyReadinessClassifications.MissingSiteMapping;
        }

        if (HasDevelopmentPlaceholderReference(policy) || MissingRequiredLegalReference(policy))
        {
            return OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified;
        }

        return verificationReadiness switch
        {
            VerificationReadiness.VerifiedProduction => OperatorConsolePolicyReadinessClassifications.ReadyVerified,
            VerificationReadiness.PilotApproved or VerificationReadiness.CompatibilityActive =>
                OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview,
            _ => OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified
        };
    }

    private static OperatorConsolePolicyReadinessEvaluation Build(
        string classification,
        bool production,
        bool policyResolved,
        OperatorConsoleResolvedStatutoryDiscountPolicy? policy,
        bool canCreateDraft,
        string? ineligibilityReason,
        string? errorCode)
    {
        var requiresManualReview = production &&
            classification != OperatorConsolePolicyReadinessClassifications.ReadyVerified;

        return new OperatorConsolePolicyReadinessEvaluation(
            classification,
            policyResolved,
            policy,
            requiresManualReview,
            canCreateDraft,
            ineligibilityReason,
            errorCode,
            OperatorMessageFor(classification));
    }

    private static bool IsProduction(string? environmentName) =>
        string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

    private static bool IsSandboxOrDevelopmentOnly(OperatorConsoleResolvedStatutoryDiscountPolicy policy)
    {
        if (policy.StatutoryDiscountPolicyId == Sandbox235APolicyId)
        {
            return true;
        }

        return HasSandboxMarker(policy.PolicyCode, policyCode: true) ||
               HasSandboxMarker(policy.PolicyName, policyCode: false) ||
               HasSandboxMarker(policy.SourceReference, policyCode: false) ||
               HasSandboxMarker(policy.LegalBasisReference, policyCode: false) ||
               HasSandboxMarker(policy.OrdinanceReference, policyCode: false) ||
               HasSandboxMarker(policy.NationalLawReference, policyCode: false);
    }

    private static bool HasSandboxMarker(string? value, bool policyCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Contains("SANDBOX", StringComparison.Ordinal) ||
            normalized.Contains("E2E", StringComparison.Ordinal) ||
            normalized.Contains("TEST", StringComparison.Ordinal))
        {
            return true;
        }

        return policyCode
            ? normalized.Contains("DEV", StringComparison.Ordinal)
            : ContainsWholeToken(normalized, "DEV");
    }

    private static bool HasDevelopmentPlaceholderReference(OperatorConsoleResolvedStatutoryDiscountPolicy policy) =>
        IsDevelopmentPlaceholder(policy.LegalBasisReference) ||
        IsDevelopmentPlaceholder(policy.OrdinanceReference) ||
        IsDevelopmentPlaceholder(policy.NationalLawReference) ||
        IsDevelopmentPlaceholder(policy.SourceReference);

    private static bool IsDevelopmentPlaceholder(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().StartsWith("DEV_PLACEHOLDER", StringComparison.OrdinalIgnoreCase);

    private static bool MissingRequiredLegalReference(OperatorConsoleResolvedStatutoryDiscountPolicy policy)
    {
        if (string.Equals(policy.PolicyLevel, "NATIONAL_LAW", StringComparison.Ordinal))
        {
            return policy.EntitlementType switch
            {
                "SENIOR_CITIZEN" => !string.Equals(policy.NationalLawReference, "RA 9994", StringComparison.OrdinalIgnoreCase),
                "PWD" => !string.Equals(policy.NationalLawReference, "RA 10754", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        return string.IsNullOrWhiteSpace(policy.OrdinanceReference) &&
               string.IsNullOrWhiteSpace(policy.LegalBasisReference);
    }

    private static bool RequiresScopedMapping(OperatorConsoleResolvedStatutoryDiscountPolicy policy) =>
        policy.PolicyLevel is "LOCAL_ORDINANCE" or "SITE_POLICY" or "OPERATIONAL_POLICY";

    private static VerificationReadiness ClassifyVerificationStatus(string? verificationStatus)
    {
        if (string.IsNullOrWhiteSpace(verificationStatus))
        {
            return VerificationReadiness.Unverified;
        }

        return verificationStatus.Trim().ToUpperInvariant() switch
        {
            "ACTIVE" => VerificationReadiness.CompatibilityActive,
            "ACTIVE_APPROVED" or "VERIFIED_OFFICIAL" => VerificationReadiness.VerifiedProduction,
            "APPROVED_FOR_PILOT" => VerificationReadiness.PilotApproved,
            "VERIFIED_SECONDARY" or "LEAD_UNVERIFIED" or "PROPOSED_ONLY" or "REJECTED" =>
                VerificationReadiness.Unverified,
            "DRAFT" or "SUSPENDED" or "SUPERSEDED" or "RETIRED" => VerificationReadiness.Inactive,
            _ => VerificationReadiness.Unverified
        };
    }

    private static bool ContainsWholeToken(string value, string token)
    {
        var start = 0;
        while (start < value.Length)
        {
            var index = value.IndexOf(token, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var before = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var afterIndex = index + token.Length;
            var after = afterIndex >= value.Length || !char.IsLetterOrDigit(value[afterIndex]);
            if (before && after)
            {
                return true;
            }

            start = index + token.Length;
        }

        return false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private enum VerificationReadiness
    {
        CompatibilityActive,
        VerifiedProduction,
        PilotApproved,
        Unverified,
        Inactive
    }

    private static string OperatorMessageFor(string classification) =>
        classification switch
        {
            OperatorConsolePolicyReadinessClassifications.ReadyVerified =>
                "The statutory discount policy is verified for production use.",
            OperatorConsolePolicyReadinessClassifications.ReadyWithManualReview =>
                "The statutory discount policy can be used only with manual review until production verification metadata is complete.",
            OperatorConsolePolicyReadinessClassifications.ConfiguredButUnverified =>
                "The statutory discount policy is configured but not verified for production use.",
            OperatorConsolePolicyReadinessClassifications.MissingRequiredPolicy =>
                "No required statutory discount production policy is configured for this entitlement.",
            OperatorConsolePolicyReadinessClassifications.MissingSiteMapping =>
                "The statutory discount policy cannot be mapped to the site or jurisdiction.",
            OperatorConsolePolicyReadinessClassifications.MissingEvidenceRule =>
                "The statutory discount policy is missing the evidence rule required by the Operator Console workflow.",
            OperatorConsolePolicyReadinessClassifications.ExpiredOrInactive =>
                "The statutory discount policy is inactive, expired, or not yet effective.",
            OperatorConsolePolicyReadinessClassifications.SandboxOnly =>
                "The statutory discount policy is sandbox or development only and cannot be used as production authority.",
            _ => "The statutory discount policy is not ready for production use."
        };
}
