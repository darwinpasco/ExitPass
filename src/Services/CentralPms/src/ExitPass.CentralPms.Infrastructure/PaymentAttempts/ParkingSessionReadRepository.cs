using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Sessions;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

public sealed class ParkingSessionReadRepository : IParkingSessionReadRepository
{
    public Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}