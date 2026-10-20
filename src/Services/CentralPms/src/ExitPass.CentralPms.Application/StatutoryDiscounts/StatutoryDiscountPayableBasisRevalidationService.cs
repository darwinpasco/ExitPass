using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.CentralPms.Application.VendorSessions;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Re-resolves the authoritative parking session and tariff immediately before an approved
/// service-channel statutory benefit is applied. Reviewer processing can outlive a transient
/// tariff quote, so application must use a fresh Central PMS-owned snapshot rather than bypassing
/// snapshot expiry.
/// </summary>
public interface IStatutoryDiscountPayableBasisRevalidationService
{
    Task<StatutoryDiscountPayableBasisRevalidationResult> RevalidateAsync(
        StatutoryDiscountServiceChannelReviewDetail review,
        Guid correlationId,
        CancellationToken cancellationToken);
}

public sealed record StatutoryDiscountPayableBasisRevalidationResult(
    bool Succeeded,
    Guid? TariffSnapshotId,
    string? ErrorCode,
    bool Retryable);

public sealed class StatutoryDiscountPayableBasisRevalidationService
    : IStatutoryDiscountPayableBasisRevalidationService
{
    private readonly IResolveVendorParkingUseCase _vendorParkingResolver;
    private readonly IParkingSessionReadRepository _parkingSessions;
    private readonly IVendorSessionProjectionRepository _projections;
    private readonly ISiteVendorAdapterRouteRegistry _routes;

    public StatutoryDiscountPayableBasisRevalidationService(
        IResolveVendorParkingUseCase vendorParkingResolver,
        IParkingSessionReadRepository parkingSessions,
        IVendorSessionProjectionRepository projections,
        ISiteVendorAdapterRouteRegistry routes)
    {
        _vendorParkingResolver = vendorParkingResolver ??
            throw new ArgumentNullException(nameof(vendorParkingResolver));
        _parkingSessions = parkingSessions ?? throw new ArgumentNullException(nameof(parkingSessions));
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<StatutoryDiscountPayableBasisRevalidationResult> RevalidateAsync(
        StatutoryDiscountServiceChannelReviewDetail review,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (!review.SiteId.HasValue ||
            !review.SiteGroupId.HasValue ||
            !review.VendorSystemId.HasValue)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_CONTEXT_INCOMPLETE");
        }

        var canonicalSession = await _parkingSessions
            .GetByIdAsync(review.ParkingSessionId, cancellationToken)
            .ConfigureAwait(false);
        if (canonicalSession is null)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_CANONICAL_SESSION_NOT_FOUND");
        }

        if (!Guid.TryParse(canonicalSession.SiteId, out var canonicalSiteId) ||
            !Guid.TryParse(canonicalSession.SiteGroupId, out var canonicalSiteGroupId) ||
            canonicalSiteId != review.SiteId.Value ||
            canonicalSiteGroupId != review.SiteGroupId.Value)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        }

        SiteVendorAdapterRoute route;
        try
        {
            route = await _routes.ResolveAsync(
                    canonicalSiteId,
                    canonicalSiteGroupId,
                    review.VendorSystemId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SiteVendorAdapterRoutingException)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_ROUTE_INVALID");
        }

        CanonicalVendorSessionProjectionIdentity? projection;
        try
        {
            projection = await _projections.FindForCanonicalSessionAsync(
                    review.ParkingSessionId,
                    canonicalSiteId,
                    canonicalSiteGroupId,
                    review.VendorSystemId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VendorSessionProjectionLookupException)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_AMBIGUOUS");
        }

        if (projection is null)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_AUTHORITATIVE_IDENTITY_UNAVAILABLE");
        }

        if (route.SiteId != canonicalSiteId ||
            route.SiteGroupId != canonicalSiteGroupId ||
            route.VendorSystemId != review.VendorSystemId.Value ||
            projection.SiteId != canonicalSiteId ||
            projection.SiteGroupId != canonicalSiteGroupId ||
            projection.VendorSystemId != review.VendorSystemId.Value ||
            projection.SourceAdapterIdentityId != route.AdapterIdentityId ||
            projection.ProjectionStatus != VendorSessionProjectionStatus.Active ||
            projection.EntryTime != canonicalSession.EntryTimestamp ||
            !SameIdentifier(projection.VendorRecordGuid, canonicalSession.VendorSessionRef))
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        }

        var ticketReference = NormalizeOperationalIdentifier(
            projection.PhysicalTicketIdentifier,
            canonicalSession.VendorSessionRef);
        var plateNumber = NormalizeOperationalIdentifier(projection.PlateNumber);

        if (ConflictsWithAuthoritativeIdentifier(
                canonicalSession.TicketNumber,
                ticketReference,
                canonicalSession.VendorSessionRef) ||
            ConflictsWithAuthoritativeIdentifier(canonicalSession.PlateNumber, plateNumber) ||
            ConflictsWithAuthoritativeIdentifier(
                review.TicketReference,
                ticketReference,
                canonicalSession.VendorSessionRef) ||
            ConflictsWithAuthoritativeIdentifier(review.PlateNumber, plateNumber))
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        }

        if (ticketReference is null && plateNumber is null)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_AUTHORITATIVE_IDENTITY_UNAVAILABLE");
        }

        var resolution = await ResolveAsync(
                review,
                ticketReference,
                ticketReference is null ? plateNumber : null,
                correlationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Outcome != ResolveVendorParkingOutcome.Resolved ||
            resolution.ParkingSession is null ||
            resolution.TariffSnapshot is null)
        {
            return Failed(
                resolution.Retryable
                    ? "STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_TEMPORARILY_UNAVAILABLE"
                    : "STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_FAILED",
                resolution.Retryable);
        }

        var session = resolution.ParkingSession;
        var snapshot = resolution.TariffSnapshot;
        if (session.ParkingSessionId != review.ParkingSessionId ||
            snapshot.ParkingSessionId != review.ParkingSessionId ||
            !Guid.TryParse(session.SiteId, out var resolvedSiteId) ||
            resolvedSiteId != review.SiteId.Value ||
            !Guid.TryParse(session.SiteGroupId, out var resolvedSiteGroupId) ||
            resolvedSiteGroupId != review.SiteGroupId.Value)
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        }

        if (snapshot.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Failed(
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_TEMPORARILY_UNAVAILABLE",
                retryable: true);
        }

        return new StatutoryDiscountPayableBasisRevalidationResult(
            Succeeded: true,
            snapshot.TariffSnapshotId,
            ErrorCode: null,
            Retryable: false);
    }

    private Task<ResolveVendorParkingResult> ResolveAsync(
        StatutoryDiscountServiceChannelReviewDetail review,
        string? ticketReference,
        string? plateNumber,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        _vendorParkingResolver.ExecuteAsync(
            new ResolveVendorParkingCommand
            {
                SiteId = review.SiteId!.Value.ToString("D"),
                SiteGroupId = review.SiteGroupId!.Value.ToString("D"),
                VendorSystemId = review.VendorSystemId!.Value.ToString("D"),
                TicketReference = ticketReference,
                PlateNumber = plateNumber,
                CorrelationId = correlationId
            },
            cancellationToken);

    private static bool ConflictsWithAuthoritativeIdentifier(
        string? candidate,
        string? authoritative,
        string? vendorSessionReference = null)
    {
        var normalized = NormalizeOperationalIdentifier(candidate, vendorSessionReference);
        return normalized is not null && !SameIdentifier(normalized, authoritative);
    }

    private static string? NormalizeOperationalIdentifier(
        string? value,
        string? vendorSessionReference = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Contains('*') ||
            normalized.StartsWith("HIKCENTRAL:", StringComparison.OrdinalIgnoreCase) ||
            SameIdentifier(normalized, vendorSessionReference))
        {
            return null;
        }

        return normalized;
    }

    private static bool SameIdentifier(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static StatutoryDiscountPayableBasisRevalidationResult Failed(
        string errorCode,
        bool retryable = false) =>
        new(Succeeded: false, TariffSnapshotId: null, errorCode, retryable);
}
