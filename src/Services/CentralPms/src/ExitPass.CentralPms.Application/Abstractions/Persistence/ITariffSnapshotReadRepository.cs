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

    /// <summary>
    /// Resolves the effective payable-basis tariff snapshot for a parking session when a statutory discount
    /// payable-basis application has already reached APPLIED.
    /// </summary>
    /// <param name="parkingSessionId">Parking session whose effective payable basis should be checked.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>
    /// The APPLIED statutory-discount payable basis when present; otherwise <see langword="null"/> when no
    /// statutory-discount applied basis exists for the session.
    /// </returns>
    Task<EffectiveTariffSnapshotResolution?> GetEffectiveAppliedTariffSnapshotAsync(
        Guid parkingSessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether a consumed tariff snapshot is tied exclusively to failed payment attempts.
    /// </summary>
    /// <param name="tariffSnapshotId">Submitted tariff snapshot identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns><c>true</c> when the snapshot was consumed only by failed attempts; otherwise, <c>false</c>.</returns>
    Task<bool> WasConsumedOnlyByFailedPaymentAttemptAsync(
        Guid tariffSnapshotId,
        CancellationToken cancellationToken);
}
