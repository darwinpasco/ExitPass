namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Persists statutory discount payable-basis applications.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter
{
    /// <summary>
    /// Applies the payable-basis result transactionally or returns an existing deterministic result.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult> ApplyAsync(
        OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand command,
        CancellationToken cancellationToken);
}
