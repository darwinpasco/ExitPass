namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Application service for reconciliation exception lifecycle operations.
/// </summary>
public interface IReconciliationExceptionLifecycleService
{
    /// <summary>Reads one reconciliation exception.</summary>
    Task<ReconciliationExceptionDetailRecord> ReadAsync(
        ReadReconciliationExceptionQuery query,
        CancellationToken cancellationToken);

    /// <summary>Assigns a reconciliation exception.</summary>
    Task<ReconciliationExceptionLifecycleResult> AssignAsync(
        AssignReconciliationExceptionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Updates reconciliation exception lifecycle status.</summary>
    Task<ReconciliationExceptionLifecycleResult> UpdateStatusAsync(
        UpdateReconciliationExceptionStatusCommand command,
        CancellationToken cancellationToken);
}
