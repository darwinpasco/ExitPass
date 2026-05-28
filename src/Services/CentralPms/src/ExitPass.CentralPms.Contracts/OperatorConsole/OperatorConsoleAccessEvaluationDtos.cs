namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for evaluating whether an operator may enter or continue a controlled Operator Console action.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationRequest(
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
/// Placeholder response body for Operator Console access evaluation.
/// </summary>
public sealed record OperatorConsoleAccessEvaluationResponse(
    Guid EvaluationId,
    bool Allowed,
    string Decision,
    IReadOnlyList<string> DenialReasons,
    string? EffectiveRole,
    OperatorConsoleDeviceTrustDto DeviceTrust,
    OperatorConsoleShiftContextDto ShiftContext,
    OperatorConsoleSiteContextDto SiteContext,
    DateTimeOffset EvaluatedAt,
    bool Persisted,
    Guid CorrelationId);

/// <summary>
/// Device trust portion of the Operator Console access evaluation response.
/// </summary>
public sealed record OperatorConsoleDeviceTrustDto(
    Guid? OperatorDeviceBindingId,
    string Status,
    string TrustLevel,
    bool Trusted);

/// <summary>
/// Shift portion of the Operator Console access evaluation response.
/// </summary>
public sealed record OperatorConsoleShiftContextDto(
    Guid? OperatorShiftId,
    string Status,
    bool Active);

/// <summary>
/// Site portion of the Operator Console access evaluation response.
/// </summary>
public sealed record OperatorConsoleSiteContextDto(
    Guid? SiteId,
    Guid? SiteGroupId,
    bool Assigned);
