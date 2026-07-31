namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementPlatformStatutoryDiscountPolicyCoverageService
    : IManagementPlatformStatutoryDiscountPolicyCoverageService
{
    private static readonly string[] SupportedEntitlements =
    [
        ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen,
        ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd
    ];

    private readonly IManagementPlatformStatutoryDiscountPolicyCoverageRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ManagementPlatformStatutoryDiscountPolicyCoverageService(
        IManagementPlatformStatutoryDiscountPolicyCoverageRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ManagementPlatformStatutoryDiscountPolicyCoverageResult> ReadCoverageAsync(
        ManagementPlatformStatutoryDiscountPolicyCoverageQuery query,
        CancellationToken cancellationToken)
    {
        var normalizedScopeType = NormalizeScopeType(query.ScopeType);
        if (!IsSupportedScopeType(normalizedScopeType))
        {
            return Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.InvalidScopeType,
                query.CorrelationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.InvalidScopeType,
                "The requested policy-coverage scope type is not supported.");
        }

        if (query.ScopeId == Guid.Empty)
        {
            return Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.InvalidScopeReference,
                query.CorrelationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.InvalidScopeReference,
                "The requested policy-coverage scope reference is invalid.");
        }

        var entitlementTypes = ResolveEntitlementTypes(query.EntitlementType);
        if (entitlementTypes is null)
        {
            return Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.MalformedAuthoritativeData,
                query.CorrelationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.MalformedAuthoritativeRecord,
                "The requested statutory entitlement type is not supported.");
        }

        var scope = await _repository.ResolveScopeAsync(
            query.ActorUserId,
            normalizedScopeType,
            query.ScopeId,
            cancellationToken);

        var scopeFailure = MapScopeFailure(scope.Status, query.CorrelationId);
        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var now = _timeProvider.GetUtcNow();
        var evaluationDate = DateOnly.FromDateTime(now.UtcDateTime);
        var candidates = await _repository.ReadPolicyCandidatesAsync(
            scope.Sites,
            entitlementTypes,
            query.IncludeInactive,
            evaluationDate,
            cancellationToken);

        var rows = scope.Sites
            .OrderBy(site => site.SiteName ?? site.SiteId.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.SiteId)
            .SelectMany(site => entitlementTypes.Select(entitlement => BuildRow(
                site,
                entitlement,
                candidates.Where(candidate => candidate.SiteId == site.SiteId &&
                    string.Equals(candidate.EntitlementType, entitlement, StringComparison.OrdinalIgnoreCase)).ToArray(),
                evaluationDate)))
            .ToArray();

        return ManagementPlatformStatutoryDiscountPolicyCoverageResult.Success(
            new ManagementPlatformStatutoryDiscountPolicyCoverage(
                normalizedScopeType,
                query.ScopeId,
                normalizedScopeType,
                query.ScopeId,
                scope.ScopeDisplayName,
                query.CorrelationId,
                now,
                rows));
    }

    private static ManagementPlatformStatutoryDiscountPolicyCoverageResult? MapScopeFailure(
        ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus status,
        Guid correlationId) =>
        status switch
        {
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved => null,
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Denied => Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeDenied,
                correlationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeDenied,
                "The caller is not authorized for the requested statutory policy scope."),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.NotFound => Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeNotFound,
                correlationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeNotFound,
                "The requested statutory policy scope was not found."),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Empty => Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.EmptyGovernedScope,
                correlationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.EmptyGovernedScope,
                "The requested statutory policy scope does not govern any Sites."),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable => Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.OrdinanceSourceUnavailable,
                correlationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.OrdinanceSourceUnavailable,
                "The Site or Site Group scope authority is unavailable.",
                retryable: true),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Malformed => Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.MalformedAuthoritativeData,
                correlationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.MalformedAuthoritativeRecord,
                "The statutory policy scope authority returned malformed data."),
            _ => Fail(
                ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.UnexpectedFailure,
                correlationId,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.UnexpectedFailure,
                "The statutory policy scope could not be evaluated.")
        };

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

    private static bool IsSupportedScopeType(string normalizedScopeType) =>
        string.Equals(normalizedScopeType, ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite, StringComparison.Ordinal) ||
        string.Equals(normalizedScopeType, ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSiteGroup, StringComparison.Ordinal);

    private static string NormalizeScopeType(string scopeType) =>
        (scopeType ?? string.Empty).Trim().Replace('-', '_').ToUpperInvariant();

    private static IReadOnlyList<string>? ResolveEntitlementTypes(string? entitlementType)
    {
        if (string.IsNullOrWhiteSpace(entitlementType))
        {
            return SupportedEntitlements;
        }

        var normalized = entitlementType.Trim().Replace('-', '_').ToUpperInvariant();
        return SupportedEntitlements.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? [normalized]
            : null;
    }

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

    private static ManagementPlatformStatutoryDiscountPolicyCoverageResult Fail(
        ManagementPlatformStatutoryDiscountPolicyCoverageOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        ManagementPlatformStatutoryDiscountPolicyCoverageResult.Failed(outcome, correlationId, errorCode, errorMessage, retryable);
}
