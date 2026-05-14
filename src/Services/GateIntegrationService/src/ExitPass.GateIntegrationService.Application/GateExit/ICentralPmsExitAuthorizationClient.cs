namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Client boundary for Central PMS gate-facing exit authorization consumption.
/// </summary>
public interface ICentralPmsExitAuthorizationClient
{
    Task<CentralPmsConsumeAuthorizationResult> ConsumeAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId,
        CancellationToken cancellationToken);
}
