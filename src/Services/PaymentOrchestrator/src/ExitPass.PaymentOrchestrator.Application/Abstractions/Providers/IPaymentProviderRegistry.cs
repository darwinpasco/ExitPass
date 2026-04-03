namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Resolves the required provider adapter for a given provider code and product code.
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
public interface IPaymentProviderRegistry
{
    /// <summary>
    /// Gets the required provider adapter for the specified provider and product.
    /// </summary>
    /// <param name="providerCode">The provider code.</param>
    /// <param name="providerProduct">The provider product code.</param>
    /// <returns>The matching provider adapter.</returns>
    IPaymentProviderAdapter GetRequired(string providerCode, string providerProduct);
}
