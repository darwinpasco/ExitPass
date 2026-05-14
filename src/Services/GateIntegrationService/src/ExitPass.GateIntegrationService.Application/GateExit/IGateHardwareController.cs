namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Hardware boundary for issuing barrier open commands.
/// </summary>
public interface IGateHardwareController
{
    Task OpenBarrierAsync(
        string gateDeviceId,
        Guid exitAuthorizationId,
        Guid correlationId,
        CancellationToken cancellationToken);
}
