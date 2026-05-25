namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Command to evaluate one reconciliation item.
/// </summary>
public sealed record EvaluateReconciliationItemCommand(
    Guid ReconciliationItemId,
    Guid? ActorUserId,
    Guid? ServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Query for one reconciliation item evaluation.
/// </summary>
public sealed record ReadReconciliationItemEvaluationQuery(Guid ReconciliationItemId);

/// <summary>
/// Command to evaluate existing reconciliation items in one run.
/// </summary>
public sealed record EvaluateReconciliationRunCommand(
    Guid ReconciliationRunId,
    Guid? ActorUserId,
    Guid? ServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Query for one reconciliation run evaluation summary.
/// </summary>
public sealed record ReadReconciliationRunEvaluationSummaryQuery(Guid ReconciliationRunId);

/// <summary>
/// Evaluation decision for one reconciliation item.
/// </summary>
public sealed record ReconciliationEvaluationDecision(
    string ItemStatus,
    string MatchStatus,
    string EvaluationClassification,
    string EvaluationReason,
    decimal? VarianceAmount,
    string? ExceptionReasonCode);

/// <summary>
/// Reconciliation item evaluation record.
/// </summary>
public sealed record ReconciliationItemEvaluationRecord(
    Guid ReconciliationItemId,
    Guid ReconciliationRunId,
    string ComparisonBasis,
    string ItemStatus,
    string MatchStatus,
    string EvaluationClassification,
    string EvaluationReason,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? VarianceAmount,
    string? ExceptionReasonCode,
    bool ExceptionCreatedOrUpdated,
    string ExceptionHandling,
    DateTimeOffset EvaluatedAt,
    Guid? CorrelationId);

/// <summary>
/// Reconciliation run evaluation summary.
/// </summary>
public sealed record ReconciliationRunEvaluationSummaryRecord(
    Guid ReconciliationRunId,
    int TotalItems,
    int EvaluatedItems,
    int MatchedItems,
    int MismatchedItems,
    int MissingSourceItems,
    int MissingTargetItems,
    int InconclusiveItems,
    int SkippedItems,
    Guid? CorrelationId);
