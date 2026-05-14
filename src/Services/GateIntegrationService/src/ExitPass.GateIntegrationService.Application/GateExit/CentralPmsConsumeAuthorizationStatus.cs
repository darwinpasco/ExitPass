namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Central PMS consume statuses understood by Gate Integration Service.
/// </summary>
public enum CentralPmsConsumeAuthorizationStatus
{
    Consumed = 0,
    NotFound = 1,
    AlreadyConsumed = 2,
    Expired = 3,
    Rejected = 4,
    Unavailable = 5
}
