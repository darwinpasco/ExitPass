namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Application service for reconciliation exception workflow operations.
/// </summary>
public interface IReconciliationWorkflowService
{
    /// <summary>Adds a note to an existing reconciliation exception.</summary>
    Task<ReconciliationNoteResult> AddNoteAsync(
        AddReconciliationNoteCommand command,
        CancellationToken cancellationToken);

    /// <summary>Submits a maker resolution request for a reconciliation exception.</summary>
    Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(
        SubmitReconciliationResolutionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Records a checker approval or rejection decision.</summary>
    Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(
        DecideReconciliationResolutionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Reads reconciliation item workflow history.</summary>
    Task<IReadOnlyList<ReconciliationWorkflowHistoryRecord>> ReadWorkflowHistoryAsync(
        ReadReconciliationWorkflowHistoryQuery query,
        CancellationToken cancellationToken);

    /// <summary>Lists reconciliation runs.</summary>
    Task<IReadOnlyList<ReconciliationRunRecord>> ListRunsAsync(
        ListReconciliationRunsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Lists reconciliation exceptions.</summary>
    Task<IReadOnlyList<ReconciliationExceptionRecord>> ListExceptionsAsync(
        ListReconciliationExceptionsQuery query,
        CancellationToken cancellationToken);
}
