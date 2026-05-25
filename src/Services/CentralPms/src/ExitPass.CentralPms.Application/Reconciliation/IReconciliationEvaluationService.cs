namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Application service for reconciliation item evaluation.
/// </summary>
public interface IReconciliationEvaluationService
{
    /// <summary>Evaluates one existing reconciliation item.</summary>
    Task<ReconciliationItemEvaluationRecord> EvaluateAsync(
        EvaluateReconciliationItemCommand command,
        CancellationToken cancellationToken);

    /// <summary>Reads the current evaluation for one existing reconciliation item.</summary>
    Task<ReconciliationItemEvaluationRecord> ReadEvaluationAsync(
        ReadReconciliationItemEvaluationQuery query,
        CancellationToken cancellationToken);
}
