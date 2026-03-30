using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

public interface ITariffSnapshotReadRepository
{
    Task<TariffSnapshot?> GetByIdAsync(Guid tariffSnapshotId, CancellationToken cancellationToken);
}