namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Application service for read-only Operator Console access evaluation.
/// </summary>
public interface IOperatorConsoleAccessEvaluationService
{
    /// <summary>
    /// Evaluates the current read model for one Operator Console workflow action.
    /// </summary>
    Task<OperatorConsoleAccessEvaluationResult> EvaluateAsync(
        OperatorConsoleAccessEvaluationCommand command,
        CancellationToken cancellationToken);
}
