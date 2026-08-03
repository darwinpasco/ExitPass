namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class ManagementPlatformStatutoryDiscountPolicyCoverageValues
{
    public const string PolicyName = "ManagementPlatformStatutoryDiscountPolicyCoverageRead";
    public const string Permission = "statutory-discount-policy.view";

    public const string ScopeTypeSite = "SITE";
    public const string ScopeTypeSiteGroup = "SITE_GROUP";

    public const string SeniorCitizen = "SENIOR_CITIZEN";
    public const string Pwd = "PWD";

    public const string ActiveCovered = "ACTIVE_COVERED";
    public const string FutureEffective = "FUTURE_EFFECTIVE";
    public const string Expired = "EXPIRED";
    public const string Inactive = "INACTIVE";
    public const string IncompleteConfiguration = "INCOMPLETE_CONFIGURATION";
    public const string NoApplicableOrdinance = "NO_APPLICABLE_ORDINANCE";
    public const string NoApplicablePolicy = "NO_APPLICABLE_POLICY";
    public const string EntitlementNotCovered = "ENTITLEMENT_NOT_COVERED";
    public const string AuthoritativeSourceUnavailable = "AUTHORITATIVE_SOURCE_UNAVAILABLE";
    public const string MalformedAuthoritativeRecord = "MALFORMED_AUTHORITATIVE_RECORD";

    public const string ScopeDenied = "SCOPE_DENIED";
    public const string ScopeNotFound = "SCOPE_NOT_FOUND";
    public const string InvalidScopeType = "INVALID_SCOPE_TYPE";
    public const string InvalidScopeReference = "INVALID_SCOPE_REFERENCE";
    public const string EmptyGovernedScope = "EMPTY_GOVERNED_SCOPE";
    public const string PolicySourceUnavailable = "POLICY_SOURCE_UNAVAILABLE";
    public const string OrdinanceSourceUnavailable = "ORDINANCE_SOURCE_UNAVAILABLE";
    public const string TransientDependencyFailure = "TRANSIENT_DEPENDENCY_FAILURE";
    public const string UnexpectedFailure = "UNEXPECTED_INTERNAL_FAILURE";

    public const string CanonicalLguCoverageSource = "CANONICAL_LGU_POLICY_COVERAGE";
    public const string DedicatedRegistrySource = "STATUTORY_POLICY_REGISTRY";
    public const string CompatibilityPolicyReferenceSource = "DISCOUNT_POLICY_REFERENCES_COMPATIBILITY";

    public const string ScopeJurisdictionSingleLgu = "SINGLE_LGU";
    public const string ScopeJurisdictionMultiLgu = "MULTI_LGU";
    public const string ScopeJurisdictionMissing = "NO_CANONICAL_LGU";
}

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageQuery(
    string ScopeType,
    Guid ScopeId,
    string? EntitlementType,
    bool IncludeInactive,
    Guid CorrelationId,
    Guid? ActorUserId);

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageResult(
    ManagementPlatformStatutoryDiscountPolicyCoverageOutcome Outcome,
    Guid CorrelationId,
    ManagementPlatformStatutoryDiscountPolicyCoverage? Coverage,
    string? ErrorCode,
    string? ErrorMessage,
    bool Retryable)
{
    public static ManagementPlatformStatutoryDiscountPolicyCoverageResult Success(
        ManagementPlatformStatutoryDiscountPolicyCoverage coverage) =>
        new(ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.Success, coverage.CorrelationId, coverage, null, null, false);

    public static ManagementPlatformStatutoryDiscountPolicyCoverageResult Failed(
        ManagementPlatformStatutoryDiscountPolicyCoverageOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        new(outcome, correlationId, null, errorCode, errorMessage, retryable);
}

public enum ManagementPlatformStatutoryDiscountPolicyCoverageOutcome
{
    Success,
    InvalidScopeType,
    InvalidScopeReference,
    ScopeDenied,
    ScopeNotFound,
    EmptyGovernedScope,
    PolicySourceUnavailable,
    OrdinanceSourceUnavailable,
    MalformedAuthoritativeData,
    TransientDependencyFailure,
    UnexpectedFailure
}

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverage(
    string RequestedScopeType,
    Guid RequestedScopeReference,
    string ResolvedScopeType,
    Guid ResolvedScopeReference,
    string? ScopeDisplayName,
    Guid CorrelationId,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageRow> CoverageRows);

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageRow(
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
    string SourceClassification,
    Guid? CanonicalJurisdictionReference = null,
    string? CanonicalJurisdictionCode = null,
    string? CanonicalJurisdictionName = null,
    string? CanonicalJurisdictionType = null,
    string? MetropolitanAreaReferences = null,
    string? ScopeJurisdictionClassification = null,
    string? BenefitType = null,
    string? BeneficiaryResidencyScope = null,
    bool? SourceDocumentAvailable = null,
    string? CoverageResolutionStatus = null);

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult(
    ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus Status,
    string? ScopeDisplayName,
    IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> Sites);

public enum ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus
{
    Resolved,
    Denied,
    NotFound,
    Empty,
    SourceUnavailable,
    Malformed
}

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageSite(
    Guid SiteId,
    Guid SiteGroupId,
    string? SiteName,
    string? SiteGroupName,
    string? LguCode,
    Guid? LocalGovernmentUnitId = null,
    string? CanonicalJurisdictionCode = null,
    string? CanonicalJurisdictionName = null,
    string? CanonicalJurisdictionType = null,
    string? MetropolitanAreaReferences = null,
    string? ScopeJurisdictionClassification = null);

public sealed record ManagementPlatformStatutoryDiscountPolicyCoverageCandidate(
    Guid SiteId,
    string EntitlementType,
    Guid? PolicyId,
    string? PolicyCode,
    string? PolicyName,
    string? PolicyStatus,
    string? VerificationStatus,
    string? PolicyLevel,
    string? PolicyResolutionBasis,
    string? LegalBasisReference,
    string? OrdinanceReference,
    string? NationalLawReference,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? SourceReference,
    DateTimeOffset? UpdatedAt,
    string SourceClassification,
    bool CoverageAvailable = true,
    bool AutoApplicationAllowed = false,
    string? CoverageResolutionStatus = null,
    string? BenefitType = null,
    string? BeneficiaryResidencyScope = null,
    bool? SourceDocumentAvailable = null);
