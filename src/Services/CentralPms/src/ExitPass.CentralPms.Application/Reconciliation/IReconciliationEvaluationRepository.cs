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

    /// <summary>Returns whether a reconciliation run exists.</summary>
    Task<bool> RunExistsAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken);

    /// <summary>Lists existing item identifiers under a reconciliation run.</summary>
    Task<IReadOnlyList<Guid>> ListRunItemIdsAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken);

    /// <summary>Lists existing items under a reconciliation run for summary readback.</summary>
    Task<IReadOnlyList<ReconciliationItemRecord>> ListRunItemsAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken);
}
