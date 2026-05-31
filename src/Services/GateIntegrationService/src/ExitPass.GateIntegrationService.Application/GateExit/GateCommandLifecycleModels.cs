namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Internal vendor-neutral gate command execution status.
/// </summary>
public enum GateCommandStatus
{
    /// <summary>
    /// Command has been requested but not started.
    /// </summary>
    Requested,

    /// <summary>
    /// Command execution has started.
    /// </summary>
    InProgress,

    /// <summary>
    /// Command execution completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Command execution failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Command execution failed and can be retried deterministically.
    /// </summary>
    Retryable,

    /// <summary>
    /// Command execution failed permanently.
    /// </summary>
    TerminalFailure
}

/// <summary>
/// Internal gate command lifecycle record.
/// </summary>
public sealed record GateCommandLifecycleRecord(
    Guid CommandId,
    Guid SourceProcessingId,
    Guid SourceEventId,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    Guid? GateDeviceId,
    string? GateDeviceIdentifier,
    Guid? LaneId,
    Guid? SiteId,
    Guid? VendorSystemId,
    GateCommandStatus CommandStatus,
    int AttemptCount,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode,
    string? FailureReason,
    Guid CorrelationId);

/// <summary>
/// Result of starting or reading an internal gate command.
/// </summary>
public sealed record GateCommandLifecycleStart(
    GateCommandLifecycleRecord Command,
    bool Created,
    bool CanInvokeAdapter);
