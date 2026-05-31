using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Vendor-neutral no-op adapter for consumed authorization handoff processing.
/// </summary>
public sealed class NoOpConsumedAuthorizationGateActionAdapter : IConsumedAuthorizationGateActionAdapter
{
    /// <inheritdoc />
    public Task ProcessConsumedAuthorizationAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        return Task.CompletedTask;
    }
}
