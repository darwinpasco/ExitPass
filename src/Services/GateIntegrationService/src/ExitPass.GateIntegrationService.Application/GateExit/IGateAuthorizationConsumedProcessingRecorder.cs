namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Records consumed authorization handoff processing outcomes for idempotency.
/// </summary>
public interface IGateAuthorizationConsumedProcessingRecorder
{
    /// <summary>
    /// Gets a previous processing record for the source event, if one exists.
    /// </summary>
    /// <param name="eventId">Source event identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Existing processing record, or <see langword="null"/>.</returns>
    Task<GateAuthorizationConsumedProcessingRecord?> GetProcessedAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful processing result.
    /// </summary>
    /// <param name="record">Processing record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordProcessedAsync(
        GateAuthorizationConsumedProcessingRecord record,
        CancellationToken cancellationToken);
}
