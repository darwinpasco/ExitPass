namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Records reportable gate exit attempt results.
/// </summary>
public interface IGateExitAttemptRecorder
{
    Task RecordAsync(
        GateExitAttemptRecord record,
        CancellationToken cancellationToken);
}
