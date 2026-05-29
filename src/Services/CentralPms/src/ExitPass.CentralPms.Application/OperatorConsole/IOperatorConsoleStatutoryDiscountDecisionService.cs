namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Coordinates access-gated Operator Console statutory discount validation decisions.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountDecisionService
{
    /// <summary>
    /// Approves or rejects an existing statutory discount validation draft without applying a discount.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountDecisionResult> DecideAsync(
        OperatorConsoleStatutoryDiscountDecisionCommand command,
        CancellationToken cancellationToken);
}
