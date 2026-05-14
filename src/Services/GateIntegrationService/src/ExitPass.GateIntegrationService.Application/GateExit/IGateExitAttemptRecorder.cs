namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Records reportable gate exit attempt results.
/// </summary>
public interface IGateExitAttemptRecorder
{
    /// <summary>
    /// Records the reportable outcome of a gate exit consume attempt.
    /// </summary>
    /// <param name="record">Attempt record to persist or publish.</param>
    /// <param name="cancellationToken">Cancellation token for the recording operation.</param>
    Task RecordAsync(
        GateExitAttemptRecord record,
        CancellationToken cancellationToken);
}
