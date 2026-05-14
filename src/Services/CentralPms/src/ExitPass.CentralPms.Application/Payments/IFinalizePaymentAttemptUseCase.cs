namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.4 Finalize Payment
///
/// Invariants Enforced:
/// - Only Central PMS may expose the application use case that finalizes PaymentAttempt state
/// </summary>
public interface IFinalizePaymentAttemptUseCase
{
    /// <summary>
    /// Finalizes the payment attempt through Central PMS after verified provider outcome processing.
    /// </summary>
    /// <param name="command">Finalization command carrying the payment attempt, terminal status, actor, and trace identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The application-level finalization result.</returns>
    Task<FinalizePaymentAttemptResult> ExecuteAsync(
        FinalizePaymentAttemptCommand command,
        CancellationToken cancellationToken);
}
