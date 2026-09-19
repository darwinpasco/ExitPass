namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Governs channel-neutral statutory parking policy discovery and selection.
/// </summary>
public static class StatutoryDiscountParkingPolicyResolutionRules
{
    /// <summary>
    /// Keeps resident-only entitlements discoverable before the customer selects one, while requiring
    /// affirmative residency confirmation before a selected resident-only policy can resolve.
    /// </summary>
    public static bool SatisfiesResidencyForResolution(
        string? requestedEntitlementType,
        bool? beneficiaryResidencySatisfied,
        string beneficiaryResidencyScope) =>
        requestedEntitlementType is null ||
        beneficiaryResidencySatisfied == true ||
        !string.Equals(beneficiaryResidencyScope, "RESIDENT_ONLY", StringComparison.Ordinal);

    /// <summary>
    /// Treats policies as competing at the same precedence only when they govern the same entitlement.
    /// </summary>
    public static bool CompetesAtSamePrecedence(
        string candidateEntitlementType,
        string selectedEntitlementType,
        int candidateScopeWeight,
        int selectedScopeWeight,
        int candidatePrecedenceRank,
        int selectedPrecedenceRank) =>
        string.Equals(candidateEntitlementType, selectedEntitlementType, StringComparison.Ordinal) &&
        candidateScopeWeight == selectedScopeWeight &&
        candidatePrecedenceRank == selectedPrecedenceRank;
}
