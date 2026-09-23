using ExitPass.CentralPms.Application.VendorParking;

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

    public StatutoryDiscountPayableBasisRevalidationService(
        IResolveVendorParkingUseCase vendorParkingResolver)
    {
        _vendorParkingResolver = vendorParkingResolver ??
            throw new ArgumentNullException(nameof(vendorParkingResolver));
    }

    public async Task<StatutoryDiscountPayableBasisRevalidationResult> RevalidateAsync(
        StatutoryDiscountServiceChannelReviewDetail review,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (!review.SiteId.HasValue ||
            !review.SiteGroupId.HasValue ||
            !review.VendorSystemId.HasValue ||
            (string.IsNullOrWhiteSpace(review.TicketReference) &&
             string.IsNullOrWhiteSpace(review.PlateNumber)))
        {
            return Failed("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_CONTEXT_INCOMPLETE");
        }

        var ticketReference = string.IsNullOrWhiteSpace(review.TicketReference)
            ? null
            : review.TicketReference;
        var resolution = await _vendorParkingResolver.ExecuteAsync(
                new ResolveVendorParkingCommand
                {
                    SiteId = review.SiteId.Value.ToString("D"),
                    SiteGroupId = review.SiteGroupId.Value.ToString("D"),
                    VendorSystemId = review.VendorSystemId.Value.ToString("D"),
                    TicketReference = ticketReference,
                    PlateNumber = ticketReference is null ? review.PlateNumber : null,
                    CorrelationId = correlationId
                },
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

    private static StatutoryDiscountPayableBasisRevalidationResult Failed(
        string errorCode,
        bool retryable = false) =>
        new(Succeeded: false, TariffSnapshotId: null, errorCode, retryable);
}
