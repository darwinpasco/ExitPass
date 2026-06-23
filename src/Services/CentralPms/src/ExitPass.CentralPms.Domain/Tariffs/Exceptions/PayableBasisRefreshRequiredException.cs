namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

/// <summary>
/// Indicates that the submitted payable basis was consumed by a failed payment attempt and must be refreshed.
/// </summary>
public sealed class PayableBasisRefreshRequiredException : Exception
{
    /// <summary>
    /// Creates the exception for a submitted tariff snapshot that can no longer support payment initiation.
    /// </summary>
    public PayableBasisRefreshRequiredException(
        Guid submittedTariffSnapshotId,
        Guid parkingSessionId)
        : base($"Tariff snapshot '{submittedTariffSnapshotId}' was consumed by a failed payment attempt. Refresh the payable basis before retrying payment.")
    {
        SubmittedTariffSnapshotId = submittedTariffSnapshotId;
        ParkingSessionId = parkingSessionId;
    }

    /// <summary>
    /// Client-submitted tariff snapshot that is no longer usable.
    /// </summary>
    public Guid SubmittedTariffSnapshotId { get; }

    /// <summary>
    /// Parking session whose payable basis must be refreshed.
    /// </summary>
    public Guid ParkingSessionId { get; }
}
