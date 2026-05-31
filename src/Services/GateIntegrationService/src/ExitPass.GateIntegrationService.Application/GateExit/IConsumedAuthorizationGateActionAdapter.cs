namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Vendor-neutral adapter boundary for acting on a consumed authorization handoff.
/// </summary>
public interface IConsumedAuthorizationGateActionAdapter
{
    /// <summary>
    /// Processes a validated consumed authorization handoff.
    /// </summary>
    /// <param name="handoff">Validated handoff payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessConsumedAuthorizationAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken);
}
