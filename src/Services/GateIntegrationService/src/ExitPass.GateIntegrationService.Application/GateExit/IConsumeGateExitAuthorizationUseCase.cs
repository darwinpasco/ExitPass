namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Use case for consuming an exit authorization and opening the gate after Central PMS approval.
/// </summary>
public interface IConsumeGateExitAuthorizationUseCase
{
    /// <summary>
    /// Executes the Central PMS consume and gate-open workflow for a presented exit authorization.
    /// </summary>
    /// <param name="command">Gate consume command containing authorization, device, service identity, and correlation data.</param>
    /// <param name="cancellationToken">Cancellation token for the workflow.</param>
    /// <returns>A gate exit consume result that reports whether the barrier was opened.</returns>
    Task<ConsumeGateExitAuthorizationResult> ExecuteAsync(
        ConsumeGateExitAuthorizationCommand command,
        CancellationToken cancellationToken);
}
