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
