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
    Task<FinalizePaymentAttemptResult> ExecuteAsync(
        FinalizePaymentAttemptCommand command,
        CancellationToken cancellationToken);
}
