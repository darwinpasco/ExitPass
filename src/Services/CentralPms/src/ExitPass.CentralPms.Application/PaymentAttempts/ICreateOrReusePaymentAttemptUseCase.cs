using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

public interface ICreateOrReusePaymentAttemptUseCase
{
    Task<CreateOrReusePaymentAttemptResult> ExecuteAsync(
        CreateOrReusePaymentAttemptCommand command,
        CancellationToken cancellationToken);
}