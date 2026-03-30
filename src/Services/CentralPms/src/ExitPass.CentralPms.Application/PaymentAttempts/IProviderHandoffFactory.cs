using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Domain.PaymentAttempts;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

public interface IProviderHandoffFactory
{
    ProviderHandoffResult CreatePlaceholder(PaymentProvider provider, Guid paymentAttemptId);
}