using ExitPass.GateIntegrationService.Application.GateExit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.Infrastructure.GateHardware;

/// <summary>
/// Default non-hardware controller used until a physical SDK adapter is configured.
/// </summary>
public sealed class NoOpGateHardwareController : IGateHardwareController
{
    public Task OpenBarrierAsync(
        string gateDeviceId,
        Guid exitAuthorizationId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

#pragma warning restore CS1591
