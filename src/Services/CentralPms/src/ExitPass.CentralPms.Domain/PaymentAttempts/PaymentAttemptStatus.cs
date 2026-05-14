namespace ExitPass.CentralPms.Domain.PaymentAttempts;

/// <summary>
/// Lifecycle state of a Central PMS payment attempt in the payment-to-exit chain.
/// </summary>
public enum PaymentAttemptStatus
{
    /// <summary>
    /// Payment attempt was created against a valid parking session and tariff snapshot.
    /// </summary>
    Initiated = 1,

    /// <summary>
    /// Payment attempt has been handed to a provider and is awaiting verified outcome.
    /// </summary>
    PendingProvider = 2,

    /// <summary>
    /// Verified provider outcome finalized the attempt as paid.
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// Verified provider outcome or timeout finalized the attempt as failed.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Payment attempt expired before confirmed provider finality.
    /// </summary>
    Expired = 5,

    /// <summary>
    /// Payment attempt was cancelled before confirmed provider finality.
    /// </summary>
    Cancelled = 6
}
