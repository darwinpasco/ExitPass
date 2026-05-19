namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

/// <summary>
/// Indicates that the tariff snapshot required to create a payment attempt was not found.
/// </summary>
public sealed class TariffSnapshotNotFoundException : Exception
{
    /// <summary>
    /// Creates the exception for the missing tariff snapshot.
    /// </summary>
    /// <param name="tariffSnapshotId">Tariff snapshot identifier that could not be found.</param>
    public TariffSnapshotNotFoundException(Guid tariffSnapshotId)
        : base($"Tariff snapshot '{tariffSnapshotId}' was not found.")
    {
        TariffSnapshotId = tariffSnapshotId;
    }

    /// <summary>
    /// Missing tariff snapshot identifier.
    /// </summary>
    public Guid TariffSnapshotId { get; }
}
