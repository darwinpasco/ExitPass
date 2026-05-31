namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Validates gate device, lane, and site scope for consumed authorization handoffs.
/// </summary>
public interface IGateAuthorizationConsumedScopeValidator
{
    /// <summary>
    /// Validates handoff scope before the adapter boundary is invoked.
    /// </summary>
    /// <param name="handoff">Consumed authorization handoff.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Scope validation result.</returns>
    Task<GateAuthorizationConsumedScopeValidationResult> ValidateAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken);
}
