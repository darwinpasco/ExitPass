namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Hardware boundary for issuing barrier open commands.
/// </summary>
public interface IGateHardwareController
{
    /// <summary>
    /// Issues a barrier-open command after Central PMS has consumed the exit authorization.
    /// </summary>
    /// <param name="gateDeviceId">Gate device identifier receiving the command.</param>
    /// <param name="exitAuthorizationId">Exit authorization identifier consumed by Central PMS.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the hardware command.</param>
    Task OpenBarrierAsync(
        string gateDeviceId,
        Guid exitAuthorizationId,
        Guid correlationId,
        CancellationToken cancellationToken);
}
