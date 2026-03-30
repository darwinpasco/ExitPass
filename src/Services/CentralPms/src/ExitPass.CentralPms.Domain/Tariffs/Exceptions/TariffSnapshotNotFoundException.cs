namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

public sealed class TariffSnapshotNotFoundException : Exception
{
    public TariffSnapshotNotFoundException(Guid tariffSnapshotId)
        : base($"Tariff snapshot '{tariffSnapshotId}' was not found.")
    {
        TariffSnapshotId = tariffSnapshotId;
    }

    public Guid TariffSnapshotId { get; }
}