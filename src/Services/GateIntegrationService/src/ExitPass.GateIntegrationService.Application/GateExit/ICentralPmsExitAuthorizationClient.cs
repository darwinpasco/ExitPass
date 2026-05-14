namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Client boundary for Central PMS gate-facing exit authorization consumption.
/// </summary>
public interface ICentralPmsExitAuthorizationClient
{
    /// <summary>
    /// Requests Central PMS to consume an exit authorization for a gate device flow.
    /// </summary>
    /// <param name="exitAuthorizationId">Exit authorization identifier to consume.</param>
    /// <param name="requestedByUserId">Service identity used as the Central PMS consume actor.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the consume request.</param>
    /// <returns>The Central PMS consume result; only <c>CONSUMED</c> authorizes a gate-open command.</returns>
    Task<CentralPmsConsumeAuthorizationResult> ConsumeAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId,
        CancellationToken cancellationToken);
}
