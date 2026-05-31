namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Processes Central PMS GateAuthorizationConsumed handoff events.
/// </summary>
public interface IGateAuthorizationConsumedHandoffHandler
{
    /// <summary>
    /// Processes a consumed authorization handoff with idempotent adapter invocation.
    /// </summary>
    /// <param name="command">Processing command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deterministic processing result.</returns>
    Task<GateAuthorizationConsumedProcessingResult> HandleAsync(
        ProcessGateAuthorizationConsumedCommand command,
        CancellationToken cancellationToken);
}
