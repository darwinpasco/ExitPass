namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Repository boundary for reconciliation exception workflow persistence and read models.
/// </summary>
public interface IReconciliationWorkflowRepository
{
    /// <summary>Adds a reconciliation exception note.</summary>
    Task<ReconciliationNoteResult> AddNoteAsync(
        AddReconciliationNoteCommand command,
        CancellationToken cancellationToken);

    /// <summary>Submits a reconciliation exception resolution request.</summary>
    Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(
        SubmitReconciliationResolutionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Records approval or rejection for a reconciliation exception resolution request.</summary>
    Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(
        DecideReconciliationResolutionCommand command,
        CancellationToken cancellationToken);

    /// <summary>Reads workflow history for a reconciliation item.</summary>
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
