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

/// <summary>
/// Optional adapter boundary for implementations that need the current gate command lifecycle record.
/// </summary>
public interface IConsumedAuthorizationGateCommandActionAdapter : IConsumedAuthorizationGateActionAdapter
{
    /// <summary>
    /// Processes a validated consumed authorization handoff with the active gate command record.
    /// </summary>
    /// <param name="command">Active gate command lifecycle record.</param>
    /// <param name="handoff">Validated handoff payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessConsumedAuthorizationAsync(
        GateCommandLifecycleRecord command,
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken);
}
