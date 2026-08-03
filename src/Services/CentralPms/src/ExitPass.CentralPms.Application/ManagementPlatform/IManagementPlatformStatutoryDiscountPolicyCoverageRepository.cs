namespace ExitPass.CentralPms.Application.ManagementPlatform;

public interface IManagementPlatformStatutoryDiscountPolicyCoverageRepository
{
    Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveScopeAsync(
        Guid? actorUserId,
        string scopeType,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveServiceSiteScopeAsync(
        Guid siteId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadPolicyCandidatesAsync(
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
        IReadOnlyList<string> entitlementTypes,
        bool includeInactive,
        DateOnly evaluationDate,
        CancellationToken cancellationToken);
}
