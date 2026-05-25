namespace ExitPass.CentralPms.Contracts.Reconciliation;

/// <summary>
/// Request body for creating a reconciliation run header.
/// </summary>
public sealed record CreateReconciliationRunRequest(
    string RunType,
    string ScopeType,
    string? RunCode,
    string? RunStatus,
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
    Guid? ServiceIdentityId);

/// <summary>
/// Response body returned after a reconciliation run is created.
/// </summary>
public sealed record CreateReconciliationRunResponse(
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
/// Reconciliation run detail response.
/// </summary>
public sealed record ReconciliationRunDetailResponse(
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
/// Paged list of reconciliation items.
/// </summary>
public sealed record ReconciliationItemsResponse(IReadOnlyList<ReconciliationItemSummary> Items);

/// <summary>
/// Reconciliation item summary.
/// </summary>
public sealed record ReconciliationItemSummary(
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
