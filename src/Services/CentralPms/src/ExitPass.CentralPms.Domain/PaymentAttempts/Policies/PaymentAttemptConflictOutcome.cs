namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

/// <summary>
/// Policy outcome for a create-or-reuse payment attempt request.
/// </summary>
public enum PaymentAttemptConflictOutcome
{
    /// <summary>
    /// No blocking attempt exists and the tariff snapshot can be bound to a new attempt.
    /// </summary>
    CreateNew = 1,

    /// <summary>
    /// The idempotency replay matches the original request and can return the same attempt.
    /// </summary>
    ReuseExisting = 2,

    /// <summary>
    /// The parking session already has an active payment attempt.
    /// </summary>
    RejectActiveAttemptExists = 3,

    /// <summary>
    /// The tariff snapshot is not eligible to back a payment attempt.
    /// </summary>
    RejectSnapshotInvalid = 4,

    /// <summary>
    /// The idempotency key was replayed with a different semantic request.
    /// </summary>
    RejectIdempotencyConflict = 5
}
