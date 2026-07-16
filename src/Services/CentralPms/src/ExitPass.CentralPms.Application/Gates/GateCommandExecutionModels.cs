namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Application service boundary for explicitly executing one canonical gate command.
/// </summary>
public interface IGateCommandExecutionService
{
    /// <summary>
    /// Executes one explicitly supplied gate command through the configured gate-action adapter.
    /// </summary>
    Task<GateCommandExecutionResult> ExecuteAsync(
        Guid gateCommandId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes one explicitly supplied due RETRYABLE gate command through the configured gate-action adapter.
    /// </summary>
    Task<GateCommandExecutionResult> RetryAsync(
        Guid gateCommandId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Persistence boundary for gate command claim and finalization.
/// </summary>
public interface IGateCommandExecutionRepository
{
    /// <summary>
    /// Claims one eligible REQUESTED command and returns the safe execution context.
    /// </summary>
    Task<GateCommandClaimResult> ClaimAsync(
        Guid gateCommandId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims one eligible due RETRYABLE command and returns the safe execution context.
    /// </summary>
    Task<GateCommandClaimResult> ClaimRetryAsync(
        Guid gateCommandId,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists one secret-free HikCentral action audit and finalizes the command lifecycle atomically.
    /// </summary>
    Task<GateCommandFinalizationResult> FinalizeAsync(
        GateCommandExecutionClaim claim,
        HikCentralGateActionResult actionResult,
        DateTimeOffset finalizedAt,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}

/// <summary>
/// Narrow execution options for this controlled fake-adapter slice.
/// </summary>
public sealed record GateCommandExecutionOptions(TimeSpan RetryDelay)
{
    /// <summary>
    /// Default fixed retry delay used to satisfy canonical RETRYABLE constraints.
    /// </summary>
    public static GateCommandExecutionOptions Default { get; } = new(TimeSpan.FromMinutes(5));
}

/// <summary>
/// Result of claiming one command for execution.
/// </summary>
public sealed record GateCommandClaimResult(
    GateCommandClaimOutcome Outcome,
    GateCommandExecutionClaim? Claim,
    string? CommandStatus,
    string? ErrorCode,
    string? Message);

/// <summary>
/// Claim outcome classification.
/// </summary>
public enum GateCommandClaimOutcome
{
    /// <summary>
    /// The command was claimed and transitioned to IN_PROGRESS.
    /// </summary>
    Claimed,

    /// <summary>
    /// The command was already completed and should not be executed again.
    /// </summary>
    AlreadyCompleted,

    /// <summary>
    /// The command is not eligible for this explicit execution slice.
    /// </summary>
    Rejected
}

/// <summary>
/// Safe command context required to invoke the HikCentral gate action boundary.
/// </summary>
public sealed record GateCommandExecutionClaim(
    Guid CommandId,
    string CommandType,
    Guid SourceProcessingId,
    Guid? SourceEventId,
    string? SourceEventRef,
    Guid GateAuthorizationConsumptionId,
    Guid ExitAuthorizationId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    Guid GateDeviceId,
    Guid? ServiceIdentityId,
    Guid? LaneId,
    Guid? SiteId,
    Guid VendorSystemId,
    Guid CorrelationId,
    int AttemptCount,
    int MaxAttempts,
    string RetryPolicyCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset LastAttemptedAt,
    string TargetResourceCode);

/// <summary>
/// Final persisted command and audit state.
/// </summary>
public sealed record GateCommandFinalizationResult(
    Guid GateCommandId,
    Guid HikCentralGateActionAuditId,
    string CommandStatus,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? TerminalFailureAt);

/// <summary>
/// Controlled execution result.
/// </summary>
public sealed record GateCommandExecutionResult(
    Guid GateCommandId,
    GateCommandExecutionOutcome Outcome,
    string CommandStatus,
    Guid? HikCentralGateActionAuditId,
    bool AdapterInvoked,
    string? ErrorCode,
    string? Message);

/// <summary>
/// Execution result classification.
/// </summary>
public enum GateCommandExecutionOutcome
{
    /// <summary>
    /// The command was claimed, the adapter was invoked, and lifecycle state was finalized.
    /// </summary>
    Executed,

    /// <summary>
    /// The command was already completed and was not executed again.
    /// </summary>
    AlreadyCompleted,

    /// <summary>
    /// The command was not eligible for execution.
    /// </summary>
    Rejected
}

/// <summary>
/// Application service boundary for one deterministic gate command dispatch cycle.
/// </summary>
public interface IGateCommandDispatchCycleService
{
    /// <summary>
    /// Selects and processes at most one eligible canonical gate command.
    /// </summary>
    Task<GateCommandDispatchCycleResult> RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read-only candidate discovery boundary for one gate command dispatch cycle.
/// </summary>
public interface IGateCommandDispatchCandidateRepository
{
    /// <summary>
    /// Finds the next eligible canonical gate command without mutating command state.
    /// </summary>
    Task<GateCommandDispatchCandidate?> FindNextEligibleAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>
/// Candidate selected for one dispatch-cycle execution attempt.
/// </summary>
public sealed record GateCommandDispatchCandidate(
    Guid GateCommandId,
    string CommandStatus,
    DateTimeOffset EligibilityTimestamp);

/// <summary>
/// Result of one deterministic dispatch cycle.
/// </summary>
public sealed record GateCommandDispatchCycleResult(
    Guid? GateCommandId,
    GateCommandDispatchCycleOutcome Outcome,
    string? CandidateStatus,
    string? FinalCommandStatus,
    Guid? HikCentralGateActionAuditId,
    bool AdapterInvoked,
    string? ErrorCode,
    string? Message);

/// <summary>
/// Dispatch-cycle outcome classification.
/// </summary>
public enum GateCommandDispatchCycleOutcome
{
    /// <summary>
    /// No eligible command was found.
    /// </summary>
    NoWork,

    /// <summary>
    /// One selected command was executed through the existing explicit execution service.
    /// </summary>
    Dispatched,

    /// <summary>
    /// The selected command became ineligible after discovery and no alternate command was processed.
    /// </summary>
    LostRaceOrIneligible
}

/// <summary>
/// Application service boundary for explicitly recovering one stale IN_PROGRESS gate command.
/// </summary>
public interface IGateCommandInProgressRecoveryService
{
    /// <summary>
    /// Recovers one stale IN_PROGRESS command without executing it.
    /// </summary>
    Task<GateCommandRecoveryResult> RecoverAsync(
        Guid gateCommandId,
        DateTimeOffset staleBefore,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}

/// <summary>
/// Persistence boundary for stale IN_PROGRESS gate command recovery.
/// </summary>
public interface IGateCommandInProgressRecoveryRepository
{
    /// <summary>
    /// Atomically recovers one eligible stale IN_PROGRESS command to RETRYABLE or TERMINAL_FAILURE.
    /// </summary>
    Task<GateCommandRecoveryResult> RecoverAsync(
        GateCommandRecoveryRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Recovery request for one explicitly supplied command.
/// </summary>
public sealed record GateCommandRecoveryRequest(
    Guid GateCommandId,
    DateTimeOffset StaleBefore,
    TimeSpan RetryDelay,
    DateTimeOffset RecoveredAt);

/// <summary>
/// Result of a stale IN_PROGRESS recovery attempt.
/// </summary>
public sealed record GateCommandRecoveryResult(
    Guid GateCommandId,
    GateCommandRecoveryOutcome Outcome,
    string CommandStatus,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? TerminalFailureAt,
    bool Mutated,
    string? ErrorCode,
    string? Message);

/// <summary>
/// Stale command recovery result classification.
/// </summary>
public enum GateCommandRecoveryOutcome
{
    /// <summary>
    /// The stale IN_PROGRESS command was recovered to RETRYABLE.
    /// </summary>
    RecoveredRetryable,

    /// <summary>
    /// The stale IN_PROGRESS command was recovered to TERMINAL_FAILURE.
    /// </summary>
    RecoveredTerminalFailure,

    /// <summary>
    /// The command had already left IN_PROGRESS and was not changed.
    /// </summary>
    AlreadyRecovered,

    /// <summary>
    /// The command was not eligible for stale recovery.
    /// </summary>
    Rejected
}
