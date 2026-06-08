namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for evaluating Operator Console access readiness.
/// </summary>
public sealed record OperatorConsoleAccessReadinessRequest(
    Guid? OperatorUserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? RequestedAction,
    string? TargetEntityType,
    Guid? TargetEntityId,
    string? WorkflowState,
    Guid? CorrelationId,
    string? IdempotencyKey,
    OperatorConsoleAccessReadinessClientContextDto? ClientContext,
    OperatorConsoleAccessReadinessDevModeContextDto? DevModeContext);

/// <summary>
/// Optional non-authoritative client context for access readiness diagnostics.
/// </summary>
public sealed record OperatorConsoleAccessReadinessClientContextDto(
    string? UiModule,
    string? ScreenState);

/// <summary>
/// Optional non-production development context for access readiness diagnostics.
/// </summary>
public sealed record OperatorConsoleAccessReadinessDevModeContextDto(
    bool UsesLocalDevFallbackContext,
    string? EnvironmentName);

/// <summary>
/// Response body for Operator Console access readiness evaluation.
/// </summary>
public sealed record OperatorConsoleAccessReadinessResponse(
    Guid? AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    string RequestedAction,
    string ReadinessStatus,
    IReadOnlyList<OperatorConsoleReadinessDimensionDto> ReadinessDimensions,
    IReadOnlyList<OperatorConsoleAccessReadinessDenialReasonDto> DenialReasons,
    OperatorConsoleOperatorReadinessDto OperatorReadiness,
    OperatorConsoleDeviceReadinessDto DeviceReadiness,
    OperatorConsoleShiftReadinessDto ShiftReadiness,
    OperatorConsoleSiteReadinessDto SiteReadiness,
    OperatorConsoleWorkflowReadinessDto WorkflowReadiness,
    bool AuditPersisted,
    DateTimeOffset EvaluatedAt,
    Guid CorrelationId,
    bool Retryable,
    string? NextOperatorAction);

/// <summary>
/// Readiness status for one required access dimension.
/// </summary>
public sealed record OperatorConsoleReadinessDimensionDto(
    string Dimension,
    string Status,
    bool Required,
    IReadOnlyList<string> DenialReasonCodes);

/// <summary>
/// Stable machine-readable readiness denial reason.
/// </summary>
public sealed record OperatorConsoleAccessReadinessDenialReasonDto(
    string Code,
    string Severity,
    bool Retryable,
    string UxMessageCategory);

/// <summary>
/// Operator identity readiness projection.
/// </summary>
public sealed record OperatorConsoleOperatorReadinessDto(
    Guid? OperatorUserId,
    string Status,
    bool Ready);

/// <summary>
/// Device readiness projection.
/// </summary>
public sealed record OperatorConsoleDeviceReadinessDto(
    Guid? OperatorDeviceBindingId,
    string Status,
    bool Ready);

/// <summary>
/// Shift readiness projection.
/// </summary>
public sealed record OperatorConsoleShiftReadinessDto(
    Guid? OperatorShiftId,
    string Status,
    bool Ready);

/// <summary>
/// Site readiness projection.
/// </summary>
public sealed record OperatorConsoleSiteReadinessDto(
    Guid? SiteId,
    Guid? SiteGroupId,
    string Status,
    bool Ready);

/// <summary>
/// Workflow-state readiness projection.
/// </summary>
public sealed record OperatorConsoleWorkflowReadinessDto(
    string RequestedAction,
    string? WorkflowState,
    string Status,
    bool Ready);
