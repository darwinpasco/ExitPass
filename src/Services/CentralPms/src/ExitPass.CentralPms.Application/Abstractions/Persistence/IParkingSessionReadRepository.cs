using ExitPass.CentralPms.Domain.Sessions;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

public interface IParkingSessionReadRepository
{
    Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken);
}