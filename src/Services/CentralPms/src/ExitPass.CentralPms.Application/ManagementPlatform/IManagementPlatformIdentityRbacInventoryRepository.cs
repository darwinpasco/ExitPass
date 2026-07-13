namespace ExitPass.CentralPms.Application.ManagementPlatform;

public interface IManagementPlatformIdentityRbacInventoryRepository
{
    Task<ManagementPlatformIdentityRbacPersistenceInventory> ReadAsync(CancellationToken cancellationToken);
}
