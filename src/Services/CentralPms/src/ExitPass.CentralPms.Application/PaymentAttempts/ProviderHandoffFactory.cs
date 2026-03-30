using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Domain.PaymentAttempts;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

public sealed class ProviderHandoffFactory : IProviderHandoffFactory
{
    public ProviderHandoffResult CreatePlaceholder(PaymentProvider provider, Guid paymentAttemptId)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new ProviderHandoffResult
        {
            Type = "REDIRECT",
            Url = $"/payments/{provider.Code.ToLowerInvariant()}/{paymentAttemptId}",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
    }
}