namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Writes Operator Console access evaluation audit evidence.
/// </summary>
public interface IOperatorConsoleAccessEvaluationWriter
{
    /// <summary>
    /// Persists one access evaluation and its denial reasons transactionally.
    /// </summary>
    Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
        OperatorConsoleAccessEvaluationResult result,
        CancellationToken cancellationToken);
}
