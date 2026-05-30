namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Application service for read-only Operator Console statutory discount policy resolution.
/// </summary>
public interface IOperatorConsoleStatutoryDiscountPolicyResolutionService
{
    /// <summary>
    /// Resolves the applicable statutory discount policy after persisting Operator Console access evaluation.
    /// </summary>
    Task<OperatorConsoleStatutoryDiscountPolicyResolutionResult> ResolveAsync(
        OperatorConsoleStatutoryDiscountPolicyResolutionCommand command,
        CancellationToken cancellationToken);
}
