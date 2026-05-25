namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Repository boundary for reconciliation item evaluation.
/// </summary>
public interface IReconciliationEvaluationRepository
{
    /// <summary>Reads one reconciliation item for evaluation.</summary>
    Task<ReconciliationItemRecord> ReadItemAsync(
        Guid reconciliationItemId,
        CancellationToken cancellationToken);

    /// <summary>Persists one reconciliation item evaluation result.</summary>
    Task<ReconciliationItemEvaluationRecord> SaveEvaluationAsync(
        EvaluateReconciliationItemCommand command,
        ReconciliationEvaluationDecision decision,
        CancellationToken cancellationToken);
}
