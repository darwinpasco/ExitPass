namespace ExitPass.GateIntegrationService.Contracts.GateExit;

/// <summary>
/// Public response from Gate Integration Service consume/open attempts.
/// </summary>
public sealed record ConsumeGateExitAuthorizationResponse(
    bool GateOpened,
    string ResultCode,
    string AuthorizationStatus,
    Guid ExitAuthorizationId,
    DateTimeOffset? ConsumedAt);
