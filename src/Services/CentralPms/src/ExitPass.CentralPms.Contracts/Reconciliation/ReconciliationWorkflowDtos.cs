namespace ExitPass.CentralPms.Contracts.Reconciliation;

/// <summary>
/// Request body for adding a reconciliation exception note.
/// </summary>
public sealed record AddReconciliationNoteRequest(
    string NoteText,
    string? NoteType,
    Guid? ActorUserId);

/// <summary>
/// Response body returned after a reconciliation exception note is persisted.
/// </summary>
public sealed record AddReconciliationNoteResponse(
    Guid ReconciliationItemId,
    Guid ReconciliationExceptionId,
    Guid ReconciliationExceptionNoteId,
    string NoteType,
    DateTimeOffset CreatedAt,
    Guid CorrelationId);

/// <summary>
/// Request body for submitting a reconciliation exception resolution request.
/// </summary>
public sealed record SubmitReconciliationResolutionRequest(
    string ResolutionAction,
    string ResolutionReason,
    string FinancialImpact,
    bool AdjustmentRequired,
    string? RequestSummary,
    string? RequestDetail,
    string? ProposedExceptionStatus,
    Guid? ActorUserId);

/// <summary>
/// Response body returned after a reconciliation exception resolution request is submitted.
/// </summary>
public sealed record SubmitReconciliationResolutionResponse(
    Guid ReconciliationItemId,
    Guid ReconciliationExceptionId,
    Guid ResolutionRequestId,
    string RequestStatus,
    string PreviousExceptionStatus,
    string ProposedExceptionStatus,
    DateTimeOffset SubmittedAt,
    Guid CorrelationId);

/// <summary>
/// Request body for approving or rejecting a reconciliation resolution request.
/// </summary>
public sealed record DecideReconciliationResolutionRequest(
    string Decision,
    string Reason,
    string? Comment,
    Guid? ActorUserId);

/// <summary>
/// Response body returned after a reconciliation resolution decision is recorded.
/// </summary>
public sealed record DecideReconciliationResolutionResponse(
    Guid ResolutionRequestId,
    Guid ReconciliationExceptionId,
    Guid ResolutionApprovalId,
    string Decision,
    string RequestStatus,
    string ExceptionStatus,
    DateTimeOffset DecidedAt,
    Guid CorrelationId);

/// <summary>
/// Reconciliation workflow history response.
/// </summary>
public sealed record ReconciliationWorkflowHistoryResponse(
    Guid ReconciliationItemId,
    IReadOnlyList<ReconciliationWorkflowHistoryEntry> Entries);

/// <summary>
/// One reconciliation workflow history entry.
/// </summary>
public sealed record ReconciliationWorkflowHistoryEntry(
    string RecordType,
    Guid? ReconciliationExceptionId,
    Guid? ReconciliationExceptionNoteId,
    Guid? ResolutionRequestId,
    Guid? ResolutionApprovalId,
    Guid? StatusHistoryId,
    Guid? ReconciliationRunId,
    Guid? ReconciliationItemId,
    string? Status,
    string? ReasonCode,
    string? Summary,
    string? Detail,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    Guid? CorrelationId);

/// <summary>
/// Paged list of reconciliation runs.
/// </summary>
public sealed record ReconciliationRunsResponse(IReadOnlyList<ReconciliationRunSummary> Runs);

/// <summary>
/// Reconciliation run summary.
/// </summary>
public sealed record ReconciliationRunSummary(
    Guid ReconciliationRunId,
    string RunCode,
    string RunType,
    string RunStatus,
    string ScopeType,
    string? SourceBatchRef,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int ItemCount,
    int MatchedCount,
    int ExceptionCount,
    Guid? CorrelationId);

/// <summary>
/// Paged list of reconciliation exceptions.
/// </summary>
public sealed record ReconciliationExceptionsResponse(IReadOnlyList<ReconciliationExceptionSummary> Exceptions);

/// <summary>
/// Reconciliation exception summary.
/// </summary>
public sealed record ReconciliationExceptionSummary(
    Guid ReconciliationExceptionId,
    Guid ReconciliationRunId,
    Guid? ReconciliationItemId,
    string RunCode,
    string ExceptionType,
    string ExceptionSeverity,
    string ExceptionStatus,
    string ExceptionReasonCode,
    string ExceptionSummary,
    Guid? PaymentAttemptId,
    Guid? PaymentConfirmationId,
    string? TargetEntityType,
    Guid? TargetEntityId,
    DateTimeOffset DetectedAt,
    Guid? CorrelationId);
