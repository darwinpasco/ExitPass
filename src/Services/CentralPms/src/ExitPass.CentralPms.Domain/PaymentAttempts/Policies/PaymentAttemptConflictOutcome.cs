namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

public enum PaymentAttemptConflictOutcome
{
    CreateNew = 1,
    ReuseExisting = 2,
    RejectActiveAttemptExists = 3,
    RejectSnapshotInvalid = 4,
    RejectIdempotencyConflict = 5
}