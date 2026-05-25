namespace ExitPass.CentralPms.Contracts.Reconciliation;

/// <summary>
/// Request body for evaluating a reconciliation item.
/// </summary>
public sealed record EvaluateReconciliationItemRequest(
    Guid? ActorUserId,
    Guid? ServiceIdentityId);

/// <summary>
/// Request body for evaluating all existing items in a reconciliation run.
/// </summary>
public sealed record EvaluateReconciliationRunRequest(
    Guid? ActorUserId,
    Guid? ServiceIdentityId);

/// <summary>
/// Reconciliation item evaluation response.
/// </summary>
public sealed record ReconciliationItemEvaluationResponse(
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
/// Reconciliation run evaluation summary response.
/// </summary>
public sealed record ReconciliationRunEvaluationSummaryResponse(
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
