namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Central PMS consume statuses understood by Gate Integration Service.
/// </summary>
public enum CentralPmsConsumeAuthorizationStatus
{
    /// <summary>
    /// Central PMS consumed the exit authorization and confirmed <c>CONSUMED</c>.
    /// </summary>
    Consumed = 0,

    /// <summary>
    /// Central PMS did not find the exit authorization.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// Central PMS reports that the authorization was already consumed.
    /// </summary>
    AlreadyConsumed = 2,

    /// <summary>
    /// Central PMS reports that the authorization has expired.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Central PMS rejected the consume request for another deterministic reason.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// Central PMS was unavailable or timed out, so Gate Integration Service must fail closed.
    /// </summary>
    Unavailable = 5
}
