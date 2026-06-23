namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

/// <summary>
/// Indicates that the submitted payable basis can no longer support payment initiation and must be refreshed.
/// </summary>
public sealed class PayableBasisRefreshRequiredException : Exception
{
    /// <summary>
    /// Creates the exception for a submitted tariff snapshot that can no longer support payment initiation.
    /// </summary>
    public PayableBasisRefreshRequiredException(
        Guid submittedTariffSnapshotId,
        Guid parkingSessionId)
        : this(
            submittedTariffSnapshotId,
            parkingSessionId,
            $"Tariff snapshot '{submittedTariffSnapshotId}' can no longer support payment initiation. Refresh the payable basis before retrying payment.")
    {
    }

    /// <summary>
    /// Creates the exception with a domain-specific refresh reason.
    /// </summary>
    public PayableBasisRefreshRequiredException(
        Guid submittedTariffSnapshotId,
        Guid parkingSessionId,
        string message)
        : base(message)
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
