namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Reads existing payment attempts by idempotency key before create-or-reuse eligibility checks.
/// </summary>
public interface IPaymentAttemptReplayReadRepository
{
    /// <summary>
    /// Finds a persisted payment attempt for an idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">Caller-supplied idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing attempt, or <see langword="null"/> when the key is unused.</returns>
    Task<PaymentAttemptReplayRecord?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);
}
