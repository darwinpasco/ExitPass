namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>Readiness evaluation command for Operator Console controlled actions.</summary>
public sealed record OperatorConsoleAccessReadinessCommand(
    Guid? OperatorUserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string RequestedAction,
    string? TargetEntityType,
    Guid? TargetEntityId,
    string? WorkflowState,
    Guid CorrelationId,
    string EnvironmentName,
    bool UsesLocalDevFallbackContext);

/// <summary>Readiness evaluation result for Operator Console controlled actions.</summary>
public sealed record OperatorConsoleAccessReadinessResult(
    bool AccessAllowed,
    string AccessDecision,
    string ReadinessStatus,
    IReadOnlyList<OperatorConsoleReadinessDimensionResult> ReadinessDimensions,
    IReadOnlyList<OperatorConsoleAccessReadinessDenialReason> DenialReasons,
    OperatorConsoleOperatorReadiness OperatorReadiness,
    OperatorConsoleDeviceReadiness DeviceReadiness,
    OperatorConsoleShiftReadiness ShiftReadiness,
    OperatorConsoleSiteReadiness SiteReadiness,
    OperatorConsoleWorkflowReadiness WorkflowReadiness,
    bool AuditPersisted,
    DateTimeOffset EvaluatedAt,
    Guid CorrelationId,
    bool Retryable,
    string? NextOperatorAction);

/// <summary>Single readiness dimension result.</summary>
public sealed record OperatorConsoleReadinessDimensionResult(
    string Dimension,
    string Status,
    bool Required,
    IReadOnlyList<string> DenialReasonCodes);

/// <summary>Stable denial reason returned by the readiness foundation.</summary>
public sealed record OperatorConsoleAccessReadinessDenialReason(
    string Code,
    string Severity,
    bool Retryable,
    string UxMessageCategory);

/// <summary>Operator identity readiness projection.</summary>
public sealed record OperatorConsoleOperatorReadiness(
    Guid? OperatorUserId,
    string Status,
    bool Ready);

/// <summary>Device readiness projection.</summary>
public sealed record OperatorConsoleDeviceReadiness(
    Guid? OperatorDeviceBindingId,
    string Status,
    bool Ready);

/// <summary>Shift readiness projection.</summary>
public sealed record OperatorConsoleShiftReadiness(
    Guid? OperatorShiftId,
    string Status,
    bool Ready);

/// <summary>Site readiness projection.</summary>
public sealed record OperatorConsoleSiteReadiness(
    Guid? SiteId,
    Guid? SiteGroupId,
    string Status,
    bool Ready);

/// <summary>Workflow-state readiness projection.</summary>
public sealed record OperatorConsoleWorkflowReadiness(
    string RequestedAction,
    string? WorkflowState,
    string Status,
    bool Ready);
