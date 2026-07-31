namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageResponse(
    string RequestedScopeType,
    Guid RequestedScopeReference,
    string ResolvedScopeType,
    Guid ResolvedScopeReference,
    string? ScopeDisplayName,
    Guid CorrelationId,
    DateTimeOffset EvaluationTimestamp,
    IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageRowDto> CoverageRows);

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageRowDto(
    Guid SiteReference,
    string? SiteDisplayName,
    string EntitlementType,
    string CoverageClassification,
    string PolicyStatusClassification,
    bool AuthoritativeCoverageAvailable,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? PolicyReference,
    string? OrdinanceOrLegalAuthorityReference,
    string? JurisdictionOrLocalityReference,
    string? PolicyVersionOrRevisionReference,
    DateTimeOffset? LastAuthoritativeUpdateTimestamp,
    string DataQualityClassification,
    string ReasonClassification,
    string SourceClassification);
