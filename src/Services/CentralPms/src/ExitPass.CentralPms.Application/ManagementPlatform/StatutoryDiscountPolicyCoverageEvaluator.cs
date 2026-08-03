namespace ExitPass.CentralPms.Application.ManagementPlatform;

internal static class StatutoryDiscountPolicyCoverageEvaluator
{
    public static IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageRow> BuildRows(
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        IReadOnlyList<string> entitlementTypes,
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate> candidates,
        DateOnly evaluationDate) =>
        sites
            .OrderBy(site => site.SiteName ?? site.SiteId.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.SiteId)
            .SelectMany(site => entitlementTypes.Select(entitlement => BuildRow(
                site,
                entitlement,
                candidates.Where(candidate => candidate.SiteId == site.SiteId &&
                    string.Equals(candidate.EntitlementType, entitlement, StringComparison.OrdinalIgnoreCase)).ToArray(),
                evaluationDate)))
            .ToArray();

    private static ManagementPlatformStatutoryDiscountPolicyCoverageRow BuildRow(
        ManagementPlatformStatutoryDiscountPolicyCoverageSite site,
        string entitlementType,
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate> candidates,
        DateOnly evaluationDate)
    {
        if (string.IsNullOrWhiteSpace(site.LguCode))
        {
            return EmptyRow(
                site,
                entitlementType,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.NoApplicableOrdinance,
                "SITE_JURISDICTION_NOT_CONFIGURED");
        }

        if (candidates.Count == 0)
        {
            return EmptyRow(
                site,
                entitlementType,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.NoApplicablePolicy,
                "NO_POLICY_RECORD_FOUND");
        }

        var malformed = candidates.FirstOrDefault(IsMalformed);
        if (malformed is not null)
        {
            return CandidateRow(
                site,
                entitlementType,
                malformed,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.MalformedAuthoritativeRecord,
                "MALFORMED_POLICY_RECORD",
                authoritativeCoverageAvailable: false);
        }

        var active = candidates
            .Where(candidate => IsActiveCovered(candidate, evaluationDate))
            .OrderBy(candidate => ScopeSpecificityRank(candidate))
            .ThenByDescending(candidate => candidate.EffectiveFrom)
            .ThenBy(candidate => candidate.PolicyCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (active is not null)
        {
            return CandidateRow(
                site,
                entitlementType,
                active,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.ActiveCovered,
                "ACTIVE_POLICY_EFFECTIVE",
                authoritativeCoverageAvailable: true);
        }

        var future = candidates
            .Where(candidate => candidate.EffectiveFrom > evaluationDate)
            .OrderBy(candidate => candidate.EffectiveFrom)
            .FirstOrDefault();
        if (future is not null)
        {
            return CandidateRow(
                site,
                entitlementType,
                future,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.FutureEffective,
                "POLICY_NOT_YET_EFFECTIVE",
                authoritativeCoverageAvailable: false);
        }

        var expired = candidates
            .Where(candidate => candidate.EffectiveTo < evaluationDate)
            .OrderByDescending(candidate => candidate.EffectiveTo)
            .FirstOrDefault();
        if (expired is not null)
        {
            return CandidateRow(
                site,
                entitlementType,
                expired,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.Expired,
                "POLICY_EXPIRED",
                authoritativeCoverageAvailable: false);
        }

        var inactive = candidates.FirstOrDefault(candidate =>
            !string.Equals(candidate.PolicyStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase));
        if (inactive is not null)
        {
            return CandidateRow(
                site,
                entitlementType,
                inactive,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.Inactive,
                "POLICY_INACTIVE",
                authoritativeCoverageAvailable: false);
        }

        return CandidateRow(
            site,
            entitlementType,
            candidates[0],
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.IncompleteConfiguration,
            "POLICY_CONFIGURATION_INCOMPLETE",
            authoritativeCoverageAvailable: false);
    }

    private static ManagementPlatformStatutoryDiscountPolicyCoverageRow EmptyRow(
        ManagementPlatformStatutoryDiscountPolicyCoverageSite site,
        string entitlementType,
        string classification,
        string reason) =>
        new(
            site.SiteId,
            site.SiteName,
            entitlementType,
            classification,
            "NONE",
            AuthoritativeCoverageAvailable: false,
            EffectiveFrom: null,
            EffectiveTo: null,
            PolicyReference: null,
            OrdinanceOrLegalAuthorityReference: null,
            JurisdictionOrLocalityReference: site.LguCode,
            PolicyVersionOrRevisionReference: null,
            LastAuthoritativeUpdateTimestamp: null,
            DataQualityClassification: "COMPLETE",
            ReasonClassification: reason,
            SourceClassification: "CENTRAL_PMS_READ_MODEL");

    private static ManagementPlatformStatutoryDiscountPolicyCoverageRow CandidateRow(
        ManagementPlatformStatutoryDiscountPolicyCoverageSite site,
        string entitlementType,
        ManagementPlatformStatutoryDiscountPolicyCoverageCandidate candidate,
        string classification,
        string reason,
        bool authoritativeCoverageAvailable) =>
        new(
            site.SiteId,
            site.SiteName,
            entitlementType,
            classification,
            candidate.PolicyStatus ?? "UNKNOWN",
            authoritativeCoverageAvailable,
            candidate.EffectiveFrom,
            candidate.EffectiveTo,
            candidate.PolicyCode ?? candidate.PolicyId?.ToString("D"),
            FirstNonBlank(candidate.OrdinanceReference, candidate.LegalBasisReference, candidate.NationalLawReference),
            site.LguCode,
            candidate.SourceReference,
            candidate.UpdatedAt,
            IsMalformed(candidate) ? "MALFORMED" : "COMPLETE",
            reason,
            candidate.SourceClassification);

    private static bool IsActiveCovered(
        ManagementPlatformStatutoryDiscountPolicyCoverageCandidate candidate,
        DateOnly evaluationDate) =>
        string.Equals(candidate.PolicyStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
        !IsMalformed(candidate) &&
        candidate.EffectiveFrom <= evaluationDate &&
        (candidate.EffectiveTo is null || candidate.EffectiveTo >= evaluationDate);

    private static bool IsMalformed(ManagementPlatformStatutoryDiscountPolicyCoverageCandidate candidate) =>
        candidate.PolicyId is null ||
        string.IsNullOrWhiteSpace(candidate.PolicyCode) ||
        string.IsNullOrWhiteSpace(candidate.PolicyStatus) ||
        string.IsNullOrWhiteSpace(candidate.EntitlementType) ||
        candidate.EffectiveFrom is null;

    private static int ScopeSpecificityRank(ManagementPlatformStatutoryDiscountPolicyCoverageCandidate candidate) =>
        candidate.PolicyLevel?.ToUpperInvariant() switch
        {
            "SITE_POLICY" => 0,
            "LOCAL_ORDINANCE" => 1,
            "OPERATIONAL_POLICY" => 2,
            "NATIONAL_LAW" => 3,
            _ => 4
        };

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
