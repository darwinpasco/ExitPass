namespace ExitPass.CentralPms.Domain.PaymentAttempts;

public enum PaymentAttemptStatus
{
    Initiated = 1,
    PendingProvider = 2,
    Confirmed = 3,
    Failed = 4,
    Expired = 5,
    Cancelled = 6
}