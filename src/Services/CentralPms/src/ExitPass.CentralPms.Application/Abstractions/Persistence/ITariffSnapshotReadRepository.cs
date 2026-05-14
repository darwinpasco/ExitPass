using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Reads tariff snapshots that can be bound to Central PMS payment attempts.
/// </summary>
public interface ITariffSnapshotReadRepository
{
    /// <summary>
    /// Finds a tariff snapshot by its canonical identifier.
    /// </summary>
    /// <param name="tariffSnapshotId">Tariff snapshot identifier supplied to the payment flow.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The tariff snapshot, or <see langword="null"/> when it is unknown to Central PMS.</returns>
    Task<TariffSnapshot?> GetByIdAsync(Guid tariffSnapshotId, CancellationToken cancellationToken);
}
