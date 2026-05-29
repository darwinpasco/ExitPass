namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Access-gated service for applying approved statutory discount validations to payable basis.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountApplyPayableBasisService
{
    /// <summary>
    /// Applies an approved statutory discount validation to payable basis when eligible.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountApplyPayableBasisResult> ApplyAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisCommand command,
        CancellationToken cancellationToken);
}
