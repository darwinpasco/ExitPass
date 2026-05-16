namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Resolves the provider product used to create a provider handoff.
/// </summary>
public interface IProviderProductResolver
{
    /// <summary>
    /// Resolves the provider product for the selected provider and payment method.
    /// </summary>
    /// <param name="providerCode">Selected payment provider code.</param>
    /// <param name="paymentMethod">Customer-selected payment method.</param>
    /// <returns>The provider product code.</returns>
    string ResolveProviderProduct(string providerCode, string paymentMethod);
}
