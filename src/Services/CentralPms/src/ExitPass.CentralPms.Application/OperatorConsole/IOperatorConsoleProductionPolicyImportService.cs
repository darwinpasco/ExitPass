namespace ExitPass.CentralPms.Application.OperatorConsole;

public interface IOperatorConsoleProductionPolicyImportService
{
    Task<ProductionPolicyImportDryRunResult> DryRunAsync(
        ProductionPolicyImportDryRunRequest request,
        CancellationToken cancellationToken);
}
