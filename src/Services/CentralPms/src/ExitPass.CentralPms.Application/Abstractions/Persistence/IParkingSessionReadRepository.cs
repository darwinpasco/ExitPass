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
}
