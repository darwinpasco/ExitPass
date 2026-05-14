using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

/// <summary>
/// Use case that creates or reuses a Central PMS payment attempt without bypassing conflict rules.
/// </summary>
public interface ICreateOrReusePaymentAttemptUseCase
{
    /// <summary>
    /// Executes the create-or-reuse flow for a payment attempt.
    /// </summary>
    /// <param name="command">Payment attempt request with session, tariff, provider, and idempotency metadata.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The created or reused payment attempt result.</returns>
    Task<CreateOrReusePaymentAttemptResult> ExecuteAsync(
        CreateOrReusePaymentAttemptCommand command,
        CancellationToken cancellationToken);
}
