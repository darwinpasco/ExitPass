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

        var rows = StatutoryDiscountPolicyCoverageEvaluator.BuildRows(
            scope.Sites,
            entitlementTypes,
            candidates,
            evaluationDate);

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

    private static ManagementPlatformStatutoryDiscountPolicyCoverageResult Fail(
        ManagementPlatformStatutoryDiscountPolicyCoverageOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        ManagementPlatformStatutoryDiscountPolicyCoverageResult.Failed(outcome, correlationId, errorCode, errorMessage, retryable);
}
