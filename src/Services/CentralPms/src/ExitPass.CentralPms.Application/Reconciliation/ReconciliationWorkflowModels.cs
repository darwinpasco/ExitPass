namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Command to add a reconciliation exception note.
/// </summary>
public sealed record AddReconciliationNoteCommand(
    Guid ReconciliationItemId,
    string NoteText,
    string NoteType,
    Guid? ActorUserId,
    Guid CorrelationId);

/// <summary>
/// Command to submit a reconciliation resolution request.
/// </summary>
public sealed record SubmitReconciliationResolutionCommand(
    Guid ReconciliationItemId,
    string ResolutionAction,
    string ResolutionReason,
    string FinancialImpact,
    bool AdjustmentRequired,
    string RequestSummary,
    string? RequestDetail,
    string ProposedExceptionStatus,
    Guid? ActorUserId,
    Guid CorrelationId);

/// <summary>
/// Command to approve or reject a reconciliation resolution request.
/// </summary>
public sealed record DecideReconciliationResolutionCommand(
    Guid ResolutionRequestId,
    string Decision,
    string Reason,
    string? Comment,
    Guid? ActorUserId,
    Guid CorrelationId);

/// <summary>
/// Query for reconciliation workflow history.
/// </summary>
public sealed record ReadReconciliationWorkflowHistoryQuery(Guid ReconciliationItemId);

/// <summary>
/// Query for reconciliation run summaries.
/// </summary>
public sealed record ListReconciliationRunsQuery(int Limit);

/// <summary>
/// Query for reconciliation exception summaries.
/// </summary>
public sealed record ListReconciliationExceptionsQuery(
    int Limit,
    string? Status,
    string? Severity,
    Guid? RunId);

/// <summary>
/// Result after a reconciliation note is added.
/// </summary>
public sealed record ReconciliationNoteResult(
    Guid ReconciliationItemId,
    Guid ReconciliationExceptionId,
    Guid ReconciliationExceptionNoteId,
    string NoteType,
    DateTimeOffset CreatedAt,
    Guid CorrelationId);

/// <summary>
/// Result after a reconciliation resolution request is submitted.
/// </summary>
public sealed record ReconciliationResolutionRequestResult(
    Guid ReconciliationItemId,
    Guid ReconciliationExceptionId,
    Guid ResolutionRequestId,
    string RequestStatus,
    string PreviousExceptionStatus,
    string ProposedExceptionStatus,
    DateTimeOffset SubmittedAt,
    Guid CorrelationId);

/// <summary>
/// Result after a reconciliation resolution decision is persisted.
/// </summary>
public sealed record ReconciliationResolutionDecisionResult(
    Guid ResolutionRequestId,
    Guid ReconciliationExceptionId,
    Guid ResolutionApprovalId,
    string Decision,
    string RequestStatus,
    string ExceptionStatus,
    DateTimeOffset DecidedAt,
    Guid CorrelationId);

/// <summary>
/// Workflow history entry.
/// </summary>
public sealed record ReconciliationWorkflowHistoryRecord(
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
/// Reconciliation run summary record.
/// </summary>
public sealed record ReconciliationRunRecord(
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
/// Reconciliation exception summary record.
/// </summary>
public sealed record ReconciliationExceptionRecord(
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
