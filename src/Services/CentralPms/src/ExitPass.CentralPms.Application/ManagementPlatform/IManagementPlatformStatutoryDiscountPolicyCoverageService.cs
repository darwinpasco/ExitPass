namespace ExitPass.CentralPms.Application.ManagementPlatform;

public interface IManagementPlatformStatutoryDiscountPolicyCoverageService
{
    Task<ManagementPlatformStatutoryDiscountPolicyCoverageResult> ReadCoverageAsync(
        ManagementPlatformStatutoryDiscountPolicyCoverageQuery query,
        CancellationToken cancellationToken);
}
