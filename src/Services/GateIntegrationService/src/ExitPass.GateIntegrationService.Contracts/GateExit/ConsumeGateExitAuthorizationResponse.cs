namespace ExitPass.GateIntegrationService.Contracts.GateExit;

/// <summary>
/// Represents the public response from Gate Integration Service consume/open attempts.
/// </summary>
/// <param name="GateOpened">Indicates whether the barrier-open command was issued.</param>
/// <param name="ResultCode">Provider-neutral result code for the consume/open attempt.</param>
/// <param name="AuthorizationStatus">Central PMS authorization status observed by Gate Integration Service.</param>
/// <param name="ExitAuthorizationId">Exit authorization identifier presented at the gate.</param>
/// <param name="ConsumedAt">Central PMS consume timestamp when consume succeeded.</param>
public sealed record ConsumeGateExitAuthorizationResponse(
    bool GateOpened,
    string ResultCode,
    string AuthorizationStatus,
    Guid ExitAuthorizationId,
    DateTimeOffset? ConsumedAt);
