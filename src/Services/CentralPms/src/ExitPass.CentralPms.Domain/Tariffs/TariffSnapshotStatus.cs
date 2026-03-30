namespace ExitPass.CentralPms.Domain.Tariffs;

public enum TariffSnapshotStatus
{
    Active = 1,
    Superseded = 2,
    Expired = 3,
    Consumed = 4,
    Invalidated = 5
}