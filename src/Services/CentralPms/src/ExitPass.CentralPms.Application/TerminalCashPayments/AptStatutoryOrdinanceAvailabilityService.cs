using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.Sessions;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

public sealed class AptStatutoryOrdinanceAvailabilityService
    : IAptStatutoryOrdinanceAvailabilityService
{
    private static readonly string[] SupportedEntitlements =
    [
        ManagementPlatformStatutoryDiscountPolicyCoverageValues.SeniorCitizen,
        ManagementPlatformStatutoryDiscountPolicyCoverageValues.Pwd
    ];

    private readonly IParkingSessionReadRepository _parkingSessions;
    private readonly IManagementPlatformStatutoryDiscountPolicyCoverageRepository _coverageRepository;
    private readonly TimeProvider _timeProvider;

    public AptStatutoryOrdinanceAvailabilityService(
        IParkingSessionReadRepository parkingSessions,
        IManagementPlatformStatutoryDiscountPolicyCoverageRepository coverageRepository,
        TimeProvider timeProvider)
    {
        _parkingSessions = parkingSessions;
        _coverageRepository = coverageRepository;
        _timeProvider = timeProvider;
    }

    public Task<AptStatutoryOrdinanceAvailabilityResult> ResolveAsync(
        AptStatutoryOrdinanceAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        EvaluateAsync("RESOLVE", request, cancellationToken);

    public Task<AptStatutoryOrdinanceAvailabilityResult> RevalidateAsync(
        AptStatutoryOrdinanceAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        EvaluateAsync("REVALIDATE", request, cancellationToken);

    private async Task<AptStatutoryOrdinanceAvailabilityResult> EvaluateAsync(
        string operation,
        AptStatutoryOrdinanceAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return validation;
        }

        var siteGroupId = Guid.Parse(request.SiteGroupId);
        var siteId = Guid.Parse(request.SiteId);
        var entitlementType = NormalizeEntitlement(request.EntitlementType)!;

        var sessionLookup = await ResolveParkingSessionAsync(siteGroupId, siteId, request, cancellationToken);
        if (sessionLookup.Status != ParkingSessionLookupStatus.Found || sessionLookup.Session is null)
        {
            return MapSessionLookupFailure(sessionLookup.Status, request.CorrelationId);
        }

        var session = sessionLookup.Session;
        if (!Matches(session.SiteGroupId, siteGroupId) || !Matches(session.SiteId, siteId))
        {
            return Fail(
                409,
                request.CorrelationId,
                AptStatutoryOrdinanceAvailabilityValues.AmbiguousScope,
                "The authoritative parking session does not match the requested APT Site scope.",
                retryable: false);
        }

        var scope = await _coverageRepository.ResolveServiceSiteScopeAsync(siteId, cancellationToken);
        var scopeFailure = MapScopeFailure(scope.Status, request.CorrelationId);
        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        if (scope.Sites.Count != 1 || scope.Sites[0].SiteGroupId != siteGroupId)
        {
            return Fail(
                409,
                request.CorrelationId,
                AptStatutoryOrdinanceAvailabilityValues.AmbiguousScope,
                "The authoritative Site scope does not match the requested APT Site Group.",
                retryable: false);
        }

        var now = _timeProvider.GetUtcNow();
        var evaluationDate = DateOnly.FromDateTime(now.UtcDateTime);
        IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate> candidates;
        try
        {
            candidates = await _coverageRepository.ReadPolicyCandidatesAsync(
                scope.Sites,
                [entitlementType],
                includeInactive: true,
                evaluationDate,
                cancellationToken);
        }
        catch
        {
            return Fail(
                503,
                request.CorrelationId,
                AptStatutoryOrdinanceAvailabilityValues.SourceUnavailable,
                "The statutory ordinance authority is temporarily unavailable.",
                retryable: true);
        }

        var row = StatutoryDiscountPolicyCoverageEvaluator.BuildRows(
                scope.Sites,
                [entitlementType],
                candidates,
                evaluationDate)
            .Single();

        var classification = MapCoverageClassification(row.CoverageClassification);
        var available = row.AuthoritativeCoverageAvailable &&
            string.Equals(classification, AptStatutoryOrdinanceAvailabilityValues.Available, StringComparison.Ordinal);
        var isRevalidate = string.Equals(operation, "REVALIDATE", StringComparison.Ordinal);

        var response = new AptStatutoryOrdinanceAvailabilityResponse(
            Operation: operation,
            RevalidationOutcome: isRevalidate
                ? available ? AptStatutoryOrdinanceAvailabilityValues.PassedUnchanged : AptStatutoryOrdinanceAvailabilityValues.Failed
                : null,
            Classification: classification,
            EntitlementType: entitlementType,
            OrdinanceCoverageAvailable: available,
            StatutoryRequestAllowed: available,
            PreCashRevalidationPassed: isRevalidate && available,
            ReadyForStatutoryCashFlow: available,
            OrdinaryPaymentPreserved: true,
            ParkingSessionId: session.ParkingSessionId,
            SiteId: siteId,
            SiteGroupId: siteGroupId,
            ResolvedScopeType: ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite,
            CoverageClassification: row.CoverageClassification,
            PolicyStatusClassification: row.PolicyStatusClassification,
            EffectiveFrom: row.EffectiveFrom,
            EffectiveTo: row.EffectiveTo,
            AuthorityClassification: row.SourceClassification,
            JurisdictionDisplayName: row.JurisdictionOrLocalityReference,
            SupportReference: row.PolicyReference,
            CorrelationId: request.CorrelationId,
            EvaluatedAt: now,
            AuthoritativeUpdatedAt: row.LastAuthoritativeUpdateTimestamp,
            Retryable: false,
            SafeMessage: ResolveSafeMessage(classification, isRevalidate));

        return new AptStatutoryOrdinanceAvailabilityResult(
            Succeeded: true,
            HttpStatusCode: 200,
            CorrelationId: request.CorrelationId,
            Response: response,
            ErrorCode: null,
            Message: null,
            Retryable: false);
    }

    private async Task<ParkingSessionLookupResult> ResolveParkingSessionAsync(
        Guid siteGroupId,
        Guid siteId,
        AptStatutoryOrdinanceAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(request.ParkingSessionId, out var parkingSessionId) && parkingSessionId != Guid.Empty)
        {
            var session = await _parkingSessions.GetByIdAsync(parkingSessionId, cancellationToken);
            return session is null
                ? new ParkingSessionLookupResult(ParkingSessionLookupStatus.NotFound, null)
                : new ParkingSessionLookupResult(ParkingSessionLookupStatus.Found, session);
        }

        if (!string.IsNullOrWhiteSpace(request.TicketReference))
        {
            return await _parkingSessions.FindByTicketReferenceAsync(
                siteGroupId,
                siteId,
                request.VendorSystemId,
                request.TicketReference,
                cancellationToken);
        }

        return await _parkingSessions.FindByPlateNumberAsync(
            siteGroupId,
            siteId,
            request.VendorSystemId,
            request.PlateNumber!,
            cancellationToken);
    }

    private static AptStatutoryOrdinanceAvailabilityResult? ValidateRequest(AptStatutoryOrdinanceAvailabilityRequest request)
    {
        if (!Guid.TryParse(request.SiteGroupId, out var siteGroupId) || siteGroupId == Guid.Empty ||
            !Guid.TryParse(request.SiteId, out var siteId) || siteId == Guid.Empty)
        {
            return Fail(400, request.CorrelationId, AptStatutoryOrdinanceAvailabilityValues.AmbiguousScope, "APT Site scope is malformed.", false);
        }

        if (NormalizeEntitlement(request.EntitlementType) is null)
        {
            return Fail(400, request.CorrelationId, AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState, "The requested statutory entitlement type is not supported.", false);
        }

        var lookupCount = 0;
        lookupCount += !string.IsNullOrWhiteSpace(request.ParkingSessionId) ? 1 : 0;
        lookupCount += !string.IsNullOrWhiteSpace(request.TicketReference) ? 1 : 0;
        lookupCount += !string.IsNullOrWhiteSpace(request.PlateNumber) ? 1 : 0;

        if (lookupCount != 1)
        {
            return Fail(400, request.CorrelationId, AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState, "Exactly one parking-session lookup mode is required.", false);
        }

        if (!string.IsNullOrWhiteSpace(request.ParkingSessionId) &&
            (!Guid.TryParse(request.ParkingSessionId, out var parkingSessionId) || parkingSessionId == Guid.Empty))
        {
            return Fail(400, request.CorrelationId, AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState, "Parking session reference is malformed.", false);
        }

        return null;
    }

    private static AptStatutoryOrdinanceAvailabilityResult? MapScopeFailure(
        ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus status,
        Guid correlationId) =>
        status switch
        {
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved => null,
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.NotFound => Fail(404, correlationId, AptStatutoryOrdinanceAvailabilityValues.AmbiguousScope, "The requested APT Site scope was not found.", false),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Empty => Fail(404, correlationId, AptStatutoryOrdinanceAvailabilityValues.AmbiguousScope, "The requested APT Site scope has no governed Site.", false),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.SourceUnavailable => Fail(503, correlationId, AptStatutoryOrdinanceAvailabilityValues.SourceUnavailable, "The statutory ordinance scope authority is temporarily unavailable.", true),
            ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Malformed => Fail(502, correlationId, AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState, "The statutory ordinance scope authority returned malformed data.", false),
            _ => Fail(500, correlationId, AptStatutoryOrdinanceAvailabilityValues.UnexpectedFailure, "The statutory ordinance scope could not be evaluated.", false)
        };

    private static AptStatutoryOrdinanceAvailabilityResult MapSessionLookupFailure(
        ParkingSessionLookupStatus status,
        Guid correlationId) =>
        status switch
        {
            ParkingSessionLookupStatus.NotFound => Fail(404, correlationId, AptStatutoryOrdinanceAvailabilityValues.SessionNotFound, "Parking session was not found.", false),
            ParkingSessionLookupStatus.Ambiguous => Fail(409, correlationId, AptStatutoryOrdinanceAvailabilityValues.AmbiguousSession, "Parking session lookup was ambiguous.", false),
            ParkingSessionLookupStatus.SourceUnavailable => Fail(503, correlationId, AptStatutoryOrdinanceAvailabilityValues.SourceUnavailable, "Parking-session authority is temporarily unavailable.", true),
            ParkingSessionLookupStatus.Malformed => Fail(502, correlationId, AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState, "Parking-session authority returned malformed data.", false),
            _ => Fail(500, correlationId, AptStatutoryOrdinanceAvailabilityValues.UnexpectedFailure, "Parking session could not be evaluated.", false)
        };

    private static string MapCoverageClassification(string coverageClassification) =>
        coverageClassification switch
        {
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.ActiveCovered => AptStatutoryOrdinanceAvailabilityValues.Available,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.NoApplicablePolicy => AptStatutoryOrdinanceAvailabilityValues.NoConfiguredPolicy,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.NoApplicableOrdinance => AptStatutoryOrdinanceAvailabilityValues.NotAvailable,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.FutureEffective => AptStatutoryOrdinanceAvailabilityValues.NotYetEffective,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.Expired => AptStatutoryOrdinanceAvailabilityValues.Expired,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.Inactive => AptStatutoryOrdinanceAvailabilityValues.Inactive,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.MalformedAuthoritativeRecord => AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState,
            ManagementPlatformStatutoryDiscountPolicyCoverageValues.IncompleteConfiguration => AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState,
            _ => AptStatutoryOrdinanceAvailabilityValues.NotAvailable
        };

    private static string ResolveSafeMessage(string classification, bool isRevalidation) =>
        classification switch
        {
            AptStatutoryOrdinanceAvailabilityValues.Available when isRevalidation => "Statutory ordinance coverage remains available for the selected Site and entitlement.",
            AptStatutoryOrdinanceAvailabilityValues.Available => "Statutory ordinance coverage is available for the selected Site and entitlement.",
            AptStatutoryOrdinanceAvailabilityValues.SourceUnavailable => "Statutory ordinance coverage could not be determined. Retry before using a statutory path.",
            AptStatutoryOrdinanceAvailabilityValues.MalformedAuthoritativeState => "Statutory ordinance coverage could not be safely evaluated.",
            _ => "Statutory ordinance coverage is not available for the selected Site and entitlement. Ordinary payment remains available when independently ready."
        };

    private static string? NormalizeEntitlement(string? entitlementType)
    {
        if (string.IsNullOrWhiteSpace(entitlementType))
        {
            return null;
        }

        var normalized = entitlementType.Trim().Replace('-', '_').ToUpperInvariant();
        return SupportedEntitlements.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    private static bool Matches(string persistedId, Guid expectedId) =>
        Guid.TryParse(persistedId, out var parsed) && parsed == expectedId;

    private static AptStatutoryOrdinanceAvailabilityResult Fail(
        int httpStatusCode,
        Guid correlationId,
        string errorCode,
        string message,
        bool retryable) =>
        new(
            Succeeded: false,
            HttpStatusCode: httpStatusCode,
            CorrelationId: correlationId,
            Response: null,
            ErrorCode: errorCode,
            Message: message,
            Retryable: retryable);
}
