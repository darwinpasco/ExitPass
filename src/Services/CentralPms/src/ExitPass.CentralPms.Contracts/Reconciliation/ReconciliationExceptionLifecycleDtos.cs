namespace ExitPass.CentralPms.Contracts.Reconciliation;

/// <summary>
/// Request body for assigning a reconciliation exception.
/// </summary>
public sealed record AssignReconciliationExceptionRequest(
    Guid? AssignedToUserId,
    Guid? AssignedToServiceIdentityId,
    string ReasonCode,
    string? Detail,
    Guid? ActorUserId,
    Guid? ServiceIdentityId);

/// <summary>
/// Request body for updating reconciliation exception status.
/// </summary>
public sealed record UpdateReconciliationExceptionStatusRequest(
    string Status,
    string ReasonCode,
    string? Detail,
    Guid? ActorUserId,
    Guid? ServiceIdentityId);

/// <summary>
/// Request body for resolving, rejecting, escalating, or closing a reconciliation exception.
/// </summary>
public sealed record ReconciliationExceptionLifecycleRequest(
    string ReasonCode,
    string? Detail,
    Guid? ActorUserId,
    Guid? ServiceIdentityId);

/// <summary>
/// Reconciliation exception detail response.
/// </summary>
public sealed record ReconciliationExceptionDetailResponse(
    Guid ReconciliationExceptionId,
    Guid ReconciliationRunId,
    Guid? ReconciliationItemId,
    Guid? IncidentRecordId,
    string ExceptionType,
    string ExceptionSeverity,
    string ExceptionStatus,
    string ExceptionReasonCode,
    string ExceptionSummary,
    string? ExceptionDetail,
    Guid? AssignedToUserId,
    Guid? AssignedToServiceIdentityId,
    string? CreatedFromStatus,
    DateTimeOffset DetectedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    string? ResolutionReasonCode,
    string? ClosureReasonCode,
    Guid? ResolvedByUserId,
    Guid? ResolvedByServiceIdentityId,
    Guid? ClosedByUserId,
    Guid? ClosedByServiceIdentityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CorrelationId);

/// <summary>
/// Response body returned after a lifecycle transition.
/// </summary>
public sealed record ReconciliationExceptionLifecycleResponse(
    Guid ReconciliationExceptionId,
    string PreviousStatus,
    string CurrentStatus,
    string Action,
    DateTimeOffset UpdatedAt,
    Guid CorrelationId);
