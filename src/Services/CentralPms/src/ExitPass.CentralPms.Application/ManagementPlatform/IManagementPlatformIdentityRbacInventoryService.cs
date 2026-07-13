namespace ExitPass.CentralPms.Application.ManagementPlatform;

public interface IManagementPlatformIdentityRbacInventoryService
{
    Task<ManagementPlatformIdentityRbacInventory> GetInventoryAsync(CancellationToken cancellationToken);
}
