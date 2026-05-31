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
    int MaxAttempts,
    string RetryPolicyCode,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset LastAttemptedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset? TerminalFailureAtUtc,
    string? FailureCode,
    string? FailureReason,
    string? LastFailureCode,
    string? LastFailureReason,
    Guid CorrelationId);

/// <summary>
/// Result of starting or reading an internal gate command.
/// </summary>
public sealed record GateCommandLifecycleStart(
    GateCommandLifecycleRecord Command,
    bool Created,
    bool CanInvokeAdapter);

/// <summary>
/// Deterministic retry policy for internal gate commands.
/// </summary>
public sealed record GateCommandRetryPolicy(
    string PolicyCode,
    int MaxAttempts,
    TimeSpan RetryDelay)
{
    /// <summary>
    /// Default bounded immediate retry policy for vendor-neutral gate commands.
    /// </summary>
    public static GateCommandRetryPolicy Default { get; } =
        new("GATE_COMMAND_RETRY_V1", MaxAttempts: 3, RetryDelay: TimeSpan.Zero);

    /// <summary>
    /// Determines whether another adapter attempt is allowed after the current attempt count.
    /// </summary>
    public bool HasAttemptsRemaining(int attemptCount) => attemptCount < MaxAttempts;
}
