namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

/// <summary>
/// Indicates that a submitted tariff snapshot is no longer the effective payable basis for the session.
/// </summary>
public sealed class StaleTariffSnapshotException : Exception
{
    /// <summary>
    /// Creates the exception with the submitted and effective tariff snapshot identifiers.
    /// </summary>
    public StaleTariffSnapshotException(
        Guid submittedTariffSnapshotId,
        Guid effectiveTariffSnapshotId,
        Guid parkingSessionId,
        Guid? statutoryDiscountApplicationId)
        : base($"Tariff snapshot '{submittedTariffSnapshotId}' is stale for parking session '{parkingSessionId}'. Effective tariff snapshot is '{effectiveTariffSnapshotId}'.")
    {
        SubmittedTariffSnapshotId = submittedTariffSnapshotId;
        EffectiveTariffSnapshotId = effectiveTariffSnapshotId;
        ParkingSessionId = parkingSessionId;
        StatutoryDiscountApplicationId = statutoryDiscountApplicationId;
    }

    /// <summary>
    /// Client-submitted stale tariff snapshot.
    /// </summary>
    public Guid SubmittedTariffSnapshotId { get; }

    /// <summary>
    /// Current effective tariff snapshot required for payment creation.
    /// </summary>
    public Guid EffectiveTariffSnapshotId { get; }

    /// <summary>
    /// Parking session whose payable basis changed.
    /// </summary>
    public Guid ParkingSessionId { get; }

    /// <summary>
    /// Statutory discount application that established the effective snapshot, when known.
    /// </summary>
    public Guid? StatutoryDiscountApplicationId { get; }
}
