namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Query for a reconciliation exception detail.
/// </summary>
public sealed record ReadReconciliationExceptionQuery(Guid ReconciliationExceptionId);

/// <summary>
/// Command to assign a reconciliation exception.
/// </summary>
public sealed record AssignReconciliationExceptionCommand(
    Guid ReconciliationExceptionId,
    Guid? AssignedToUserId,
    Guid? AssignedToServiceIdentityId,
    string ReasonCode,
    string? Detail,
    Guid? ActorUserId,
    Guid? ServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Command to update a reconciliation exception lifecycle status.
/// </summary>
public sealed record UpdateReconciliationExceptionStatusCommand(
    Guid ReconciliationExceptionId,
    string NewStatus,
    string Action,
    string ReasonCode,
    string? Detail,
    Guid? ActorUserId,
    Guid? ServiceIdentityId,
    Guid CorrelationId);

/// <summary>
/// Reconciliation exception detail record.
/// </summary>
public sealed record ReconciliationExceptionDetailRecord(
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
/// Result after a reconciliation exception lifecycle transition.
/// </summary>
public sealed record ReconciliationExceptionLifecycleResult(
    Guid ReconciliationExceptionId,
    string PreviousStatus,
    string CurrentStatus,
    string Action,
    DateTimeOffset UpdatedAt,
    Guid CorrelationId);
