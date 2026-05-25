namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Command to create a reconciliation run header.
/// </summary>
public sealed record CreateReconciliationRunCommand(
    string RunType,
    string ScopeType,
    string? RunCode,
    string RunStatus,
    Guid? SiteGroupId,
    Guid? SiteId,
    Guid? IncidentRecordId,
    Guid? PaymentRailId,
    Guid? VendorSystemId,
    string? SourceBatchRef,
    DateTimeOffset? WindowStartAt,
    DateTimeOffset? WindowEndAt,
    bool GenerateItems,
    Guid? ActorUserId,
    Guid? ServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Query for a reconciliation run detail.
/// </summary>
public sealed record ReadReconciliationRunQuery(Guid ReconciliationRunId);

/// <summary>
/// Query for reconciliation items belonging to a run.
/// </summary>
public sealed record ListReconciliationRunItemsQuery(Guid ReconciliationRunId, int Limit);

/// <summary>
/// Query for a single reconciliation item.
/// </summary>
public sealed record ReadReconciliationItemQuery(Guid ReconciliationItemId);

/// <summary>
/// Result after creating a reconciliation run.
/// </summary>
public sealed record ReconciliationRunCreateResult(
    Guid ReconciliationRunId,
    string RunCode,
    string RunType,
    string RunStatus,
    string ScopeType,
    int ItemCount,
    bool ItemGenerationPerformed,
    string ItemGenerationMessage,
    Guid CorrelationId);

/// <summary>
/// Reconciliation run detail record.
/// </summary>
public sealed record ReconciliationRunDetailRecord(
    Guid ReconciliationRunId,
    string RunCode,
    string RunType,
    string RunStatus,
    string ScopeType,
    Guid? SiteGroupId,
    Guid? SiteId,
    Guid? IncidentRecordId,
    Guid? PaymentRailId,
    Guid? VendorSystemId,
    string? SourceBatchRef,
    DateTimeOffset? WindowStartAt,
    DateTimeOffset? WindowEndAt,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? FailureReasonCode,
    int ItemCount,
    int MatchedCount,
    int ExceptionCount,
    int RejectedCount,
    int DisputedCount,
    Guid? InitiatedByUserId,
    Guid? InitiatedByServiceIdentityId,
    Guid? CorrelationId);

/// <summary>
/// Reconciliation item detail record.
/// </summary>
public sealed record ReconciliationItemRecord(
    Guid ReconciliationItemId,
    Guid ReconciliationRunId,
    Guid? MopsTransactionRecordId,
    Guid? ManualGateLogId,
    Guid? PaymentAttemptId,
    Guid? PaymentConfirmationId,
    Guid? ProviderOutcomeId,
    string? TargetEntityType,
    Guid? TargetEntityId,
    string ComparisonBasis,
    string ItemStatus,
    string MatchStatus,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    string? CurrencyCode,
    decimal? VarianceAmount,
    string? ExceptionReasonCode,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CorrelationId);
