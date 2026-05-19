using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Domain.PaymentAttempts;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

/// <summary>
/// Default provider handoff factory for Central PMS payment attempt responses.
/// </summary>
public sealed class ProviderHandoffFactory : IProviderHandoffFactory
{
    /// <inheritdoc />
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
