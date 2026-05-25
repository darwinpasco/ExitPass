namespace ExitPass.CentralPms.Contracts.Reconciliation;

/// <summary>
/// Request body for evaluating a reconciliation item.
/// </summary>
public sealed record EvaluateReconciliationItemRequest(
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
