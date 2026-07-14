namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Read-only preflight boundary for determining whether payment finality is verified
/// before ExitAuthorization issuance.
/// </summary>
public interface IExitAuthorizationPaymentFinalityReadRepository
{
    /// <summary>
    /// Returns whether the payment attempt has confirmed finality for the requested parking session.
    /// </summary>
    /// <param name="parkingSessionId">Parking session for which exit authorization is requested.</param>
    /// <param name="paymentAttemptId">Payment attempt backing the authorization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the payment attempt is confirmed and has recorded payment evidence.</returns>
    Task<bool> IsPaymentFinalityVerifiedAsync(
        Guid parkingSessionId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Local fallback used only when no persistence-backed preflight repository is provided.
/// </summary>
public sealed class OptimisticExitAuthorizationPaymentFinalityReadRepository : IExitAuthorizationPaymentFinalityReadRepository
{
    public static readonly OptimisticExitAuthorizationPaymentFinalityReadRepository Instance = new();

    private OptimisticExitAuthorizationPaymentFinalityReadRepository()
    {
    }

    public Task<bool> IsPaymentFinalityVerifiedAsync(
        Guid parkingSessionId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
