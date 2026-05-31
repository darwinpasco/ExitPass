namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Records the internal vendor-neutral gate command lifecycle for consumed authorization handoffs.
/// </summary>
public interface IGateCommandLifecycleRecorder
{
    /// <summary>
    /// Starts a gate command or returns the current command for the handoff processing key.
    /// </summary>
    Task<GateCommandLifecycleStart> BeginCommandAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a command as completed successfully.
    /// </summary>
    Task RecordSucceededAsync(
        Guid commandId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a command as failed.
    /// </summary>
    Task RecordFailedAsync(
        Guid commandId,
        string failureCode,
        string failureReason,
        bool retryable,
        CancellationToken cancellationToken);
}
