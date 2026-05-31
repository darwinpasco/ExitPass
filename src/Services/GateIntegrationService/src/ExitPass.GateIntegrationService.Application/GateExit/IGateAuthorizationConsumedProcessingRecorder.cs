namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Records consumed authorization handoff processing outcomes for idempotency.
/// </summary>
public interface IGateAuthorizationConsumedProcessingRecorder
{
    /// <summary>
    /// Starts durable processing or returns the current state for an already-seen handoff.
    /// </summary>
    /// <param name="handoff">Consumed authorization handoff.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current processing state.</returns>
    Task<GateAuthorizationConsumedProcessingStart> BeginProcessingAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful processing result.
    /// </summary>
    /// <param name="record">Processing record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordProcessedAsync(
        GateAuthorizationConsumedProcessingRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed processing attempt.
    /// </summary>
    /// <param name="handoff">Consumed authorization handoff.</param>
    /// <param name="failureCode">Deterministic failure code.</param>
    /// <param name="failureReason">Failure reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordFailedAsync(
        GateAuthorizationConsumedHandoff handoff,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken);
}
