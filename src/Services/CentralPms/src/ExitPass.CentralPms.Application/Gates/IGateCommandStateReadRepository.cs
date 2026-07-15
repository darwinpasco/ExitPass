namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Read-only inventory over canonical gate consumption, processing, command, and vendor audit records.
/// </summary>
public interface IGateCommandStateReadRepository
{
    /// <summary>
    /// Reads the canonical gate command state chain for one gate authorization consumption.
    /// </summary>
    Task<GateCommandStateReadModel?> GetByConsumptionIdAsync(
        Guid gateAuthorizationConsumptionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Canonical gate command state chain for a consumed authorization.
/// </summary>
public sealed record GateCommandStateReadModel(
    GateAuthorizationConsumptionReadModel Consumption,
    GateAuthorizationConsumedProcessingReadModel? ConsumedProcessing,
    GateCommandReadModel? GateCommand,
    IReadOnlyList<HikCentralGateActionAuditReadModel> HikCentralActionAttempts);

/// <summary>
/// Read-only gate authorization consumption facts.
/// </summary>
public sealed record GateAuthorizationConsumptionReadModel(
    Guid GateAuthorizationConsumptionId,
    Guid? ExitAuthorizationId,
    Guid? GateDeviceId,
    Guid SiteId,
    Guid? LaneId,
    string ConsumeStatus,
    DateTimeOffset? ConsumedAt,
    Guid? CorrelationId);

/// <summary>
/// Read-only consumed-processing inbox facts.
/// </summary>
public sealed record GateAuthorizationConsumedProcessingReadModel(
    Guid ProcessingId,
    Guid ProcessingKey,
    Guid? EventId,
    string EventType,
    string ProcessingStatus,
    string ProcessingResult,
    int AttemptCount,
    DateTimeOffset FirstAttemptedAt,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? ProcessedAt,
    string? FailureCode,
    string? FailureReason);

/// <summary>
/// Read-only vendor-neutral gate command lifecycle facts.
/// </summary>
public sealed record GateCommandReadModel(
    Guid CommandId,
    string CommandType,
    string CommandStatus,
    int AttemptCount,
    int MaxAttempts,
    string RetryPolicyCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset LastAttemptedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? TerminalFailureAt,
    string? FailureCode,
    string? FailureReason,
    string? LastFailureCode,
    string? LastFailureReason);

/// <summary>
/// Secret-free read-only HikCentral gate action attempt audit facts.
/// </summary>
public sealed record HikCentralGateActionAuditReadModel(
    Guid HikCentralGateActionAuditId,
    string VendorCode,
    string VendorOperation,
    string DoorIndexCode,
    string RequestMethod,
    string RequestPath,
    string RequestHash,
    string SignedHeaderNames,
    Guid RequestCorrelationId,
    string? VendorCorrelationId,
    int? HttpStatusCode,
    string? VendorResultCode,
    string? VendorResultMessage,
    string ActionOutcome,
    bool Retryable,
    bool FailureRecorded,
    int DurationMs,
    bool TimedOut,
    bool VendorUnavailable,
    bool TransportFailure,
    DateTimeOffset RequestedAt,
    DateTimeOffset RespondedAt);
