using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

public sealed class TariffSnapshotReadRepository : ITariffSnapshotReadRepository
{
    public Task<TariffSnapshot?> GetByIdAsync(Guid tariffSnapshotId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}