namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command to evaluate whether an operator may perform a controlled Operator Console action.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationCommand(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    string WorkflowCode,
    string ControlledActionCode,
    Guid? ParkingSessionId,
    string? EvidenceAccessIntent,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Read-only access evaluation result.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationResult(
    Guid EvaluationId,
    bool Allowed,
    string Decision,
    IReadOnlyList<string> DenialReasons,
    string? EffectiveRole,
    OperatorConsoleDeviceTrustResult DeviceTrust,
    OperatorConsoleShiftContextResult ShiftContext,
    OperatorConsoleSiteContextResult SiteContext,
    DateTimeOffset EvaluatedAt,
    bool Persisted,
    Guid CorrelationId,
    OperatorConsoleAccessEvaluationPersistenceContext PersistenceContext);

/// <summary>
/// Device trust result for access evaluation.
/// </summary>
public sealed record OperatorConsoleDeviceTrustResult(
    Guid? OperatorDeviceBindingId,
    string Status,
    string TrustLevel,
    bool Trusted);

/// <summary>
/// Shift context result for access evaluation.
/// </summary>
public sealed record OperatorConsoleShiftContextResult(
    Guid? OperatorShiftId,
    string Status,
    bool Active);

/// <summary>
/// Site context result for access evaluation.
/// </summary>
public sealed record OperatorConsoleSiteContextResult(
    Guid? SiteId,
    Guid? SiteGroupId,
    bool Assigned);

/// <summary>
/// Persistence-only context captured with an Operator Console access evaluation.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationPersistenceContext(
    Guid OperatorUserId,
    Guid? HrIdentityMappingId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    Guid? ShiftTakeoverId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string RequestedAction,
    string WorkflowCode,
    string? TargetEntityType,
    Guid? TargetEntityId);
