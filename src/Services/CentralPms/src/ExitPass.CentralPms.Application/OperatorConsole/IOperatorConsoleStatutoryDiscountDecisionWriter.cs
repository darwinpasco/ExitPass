namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Persists Operator Console statutory discount validation decisions.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountDecisionWriter
{
    /// <summary>
    /// Transitions an existing statutory discount validation draft to a terminal review decision.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDecisionPersistenceResult> PersistAsync(
        OperatorConsoleStatutoryDiscountDecisionPersistenceCommand command,
        CancellationToken cancellationToken);
}
