namespace ExitPass.CentralPms.Domain.Sessions;

public enum ParkingSessionStatus
{
    Open = 1,
    PaymentRequired = 2,
    PaymentInProgress = 3,
    Paid = 4,
    ExitAuthorized = 5,
    Exited = 6,
    Closed = 7
}