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
    private readonly IReadOnlyDictionary<string, PaymentProviderAdapterRegistration> _registrationsByKey;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentProviderRegistry"/> class.
    /// </summary>
    /// <param name="registrations">The registered provider adapter factories.</param>
    /// <param name="serviceProvider">The scoped service provider used for lazy adapter construction.</param>
    public PaymentProviderRegistry(
        IEnumerable<PaymentProviderAdapterRegistration> registrations,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _registrationsByKey = registrations.ToDictionary(
            BuildKey,
            StringComparer.OrdinalIgnoreCase);
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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

        if (_registrationsByKey.TryGetValue(key, out var registration))
        {
            return registration.CreateAdapter(_serviceProvider);
        }

        throw new InvalidOperationException(
            $"No payment provider adapter is registered for provider '{providerCode}' and product '{providerProduct}'.");
    }

    private static string BuildKey(PaymentProviderAdapterRegistration registration)
    {
        return BuildKey(registration.ProviderCode, registration.ProviderProduct);
    }

    private static string BuildKey(string providerCode, string providerProduct)
    {
        return $"{providerCode.Trim()}::{providerProduct.Trim()}";
    }
}

/// <summary>
/// Describes a lazily constructed provider adapter registration.
/// </summary>
/// <param name="ProviderCode">Provider code implemented by the adapter.</param>
/// <param name="ProviderProduct">Provider product code implemented by the adapter.</param>
/// <param name="CreateAdapter">Factory that resolves the adapter only when selected.</param>
public sealed record PaymentProviderAdapterRegistration(
    string ProviderCode,
    string ProviderProduct,
    Func<IServiceProvider, IPaymentProviderAdapter> CreateAdapter);
