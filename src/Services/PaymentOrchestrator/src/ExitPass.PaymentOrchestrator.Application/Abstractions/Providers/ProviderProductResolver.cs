using ExitPass.PaymentOrchestrator.Contracts.Providers;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Resolves provider product codes for currently supported WebPay providers.
/// </summary>
public sealed class ProviderProductResolver : IProviderProductResolver
{
    /// <inheritdoc />
    public string ResolveProviderProduct(string providerCode, string paymentMethod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentMethod);

        return providerCode.Trim().ToUpperInvariant() switch
        {
            ProviderCode.Aub => ProviderProductCode.AubCardCashier,
            ProviderCode.PayMongo => ProviderProductCode.PayMongoCheckoutSession,
            _ => throw new InvalidOperationException(
                $"No provider product is configured for provider '{providerCode}'.")
        };
    }
}
