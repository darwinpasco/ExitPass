namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Use case for consuming an exit authorization and opening the gate after Central PMS approval.
/// </summary>
public interface IConsumeGateExitAuthorizationUseCase
{
    Task<ConsumeGateExitAuthorizationResult> ExecuteAsync(
        ConsumeGateExitAuthorizationCommand command,
        CancellationToken cancellationToken);
}
