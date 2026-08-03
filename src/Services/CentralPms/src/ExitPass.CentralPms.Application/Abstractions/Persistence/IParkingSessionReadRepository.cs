using ExitPass.CentralPms.Domain.Sessions;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Reads parking sessions used as the authority anchor for payment attempt creation.
/// </summary>
public interface IParkingSessionReadRepository
{
    /// <summary>
    /// Finds a parking session by its canonical identifier.
    /// </summary>
    /// <param name="parkingSessionId">Parking session identifier supplied to the payment flow.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The parking session, or <see langword="null"/> when it is unknown to Central PMS.</returns>
    Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the canonical parking session by ticket reference within the supplied APT scope.
    /// </summary>
    Task<ParkingSessionLookupResult> FindByTicketReferenceAsync(
        Guid siteGroupId,
        Guid siteId,
        string? vendorSystemId,
        string ticketReference,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the canonical parking session by plate number within the supplied APT scope.
    /// </summary>
    Task<ParkingSessionLookupResult> FindByPlateNumberAsync(
        Guid siteGroupId,
        Guid siteId,
        string? vendorSystemId,
        string plateNumber,
        CancellationToken cancellationToken);
}

public sealed record ParkingSessionLookupResult(
    ParkingSessionLookupStatus Status,
    ParkingSession? Session);

public enum ParkingSessionLookupStatus
{
    Found,
    NotFound,
    Ambiguous,
    SourceUnavailable,
    Malformed
}
