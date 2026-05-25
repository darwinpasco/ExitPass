namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Repository boundary for reconciliation run and item operations.
/// </summary>
public interface IReconciliationRunItemRepository
{
    /// <summary>Creates one reconciliation run header.</summary>
    Task<ReconciliationRunCreateResult> CreateRunAsync(
        CreateReconciliationRunCommand command,
        CancellationToken cancellationToken);

    /// <summary>Reads one reconciliation run.</summary>
    Task<ReconciliationRunDetailRecord> ReadRunAsync(
        ReadReconciliationRunQuery query,
        CancellationToken cancellationToken);

    /// <summary>Lists items for one reconciliation run.</summary>
    Task<IReadOnlyList<ReconciliationItemRecord>> ListRunItemsAsync(
        ListReconciliationRunItemsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Reads one reconciliation item.</summary>
    Task<ReconciliationItemRecord> ReadItemAsync(
        ReadReconciliationItemQuery query,
        CancellationToken cancellationToken);
}
