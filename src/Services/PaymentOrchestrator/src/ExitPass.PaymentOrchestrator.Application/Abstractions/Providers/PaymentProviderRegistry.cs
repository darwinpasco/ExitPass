using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers;

/// <summary>
/// Resolves the required provider adapter using provider code and provider product code.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 4.2.7 Payment Orchestrator
///
/// Invariants Enforced:
/// - Provider adapter resolution must be explicit and deterministic.
/// </summary>
public sealed class PaymentProviderRegistry : IPaymentProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPaymentProviderAdapter> _adaptersByKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentProviderRegistry"/> class.
    /// </summary>
    /// <param name="adapters">The registered provider adapters.</param>
    public PaymentProviderRegistry(IEnumerable<IPaymentProviderAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        _adaptersByKey = adapters.ToDictionary(
            BuildKey,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IPaymentProviderAdapter GetRequired(string providerCode, string providerProduct)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            throw new ArgumentException("Provider code is required.", nameof(providerCode));
        }

        if (string.IsNullOrWhiteSpace(providerProduct))
        {
            throw new ArgumentException("Provider product is required.", nameof(providerProduct));
        }

        var key = BuildKey(providerCode, providerProduct);

        if (_adaptersByKey.TryGetValue(key, out var adapter))
        {
            return adapter;
        }

        throw new InvalidOperationException(
            $"No payment provider adapter is registered for provider '{providerCode}' and product '{providerProduct}'.");
    }

    private static string BuildKey(IPaymentProviderAdapter adapter)
    {
        return BuildKey(adapter.ProviderCode, adapter.ProviderProduct);
    }

    private static string BuildKey(string providerCode, string providerProduct)
    {
        return $"{providerCode.Trim()}::{providerProduct.Trim()}";
    }
}
