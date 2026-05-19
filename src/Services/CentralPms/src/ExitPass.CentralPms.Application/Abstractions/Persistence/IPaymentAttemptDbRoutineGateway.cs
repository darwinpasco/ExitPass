namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Gateway to the Central PMS database routine that creates or reuses payment attempts.
/// </summary>
public interface IPaymentAttemptDbRoutineGateway
{
    /// <summary>
    /// Creates a new payment attempt or returns the deterministic idempotent result from storage.
    /// </summary>
    /// <param name="request">Payment attempt request passed to the authoritative routine.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative create-or-reuse result.</returns>
    Task<CreateOrReusePaymentAttemptDbResult> CreateOrReusePaymentAttemptAsync(
        CreateOrReusePaymentAttemptDbRequest request,
        CancellationToken cancellationToken);
}
