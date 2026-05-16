using ExitPass.PaymentOrchestrator.Contracts.Routing;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Resolves the configured payment provider route for a payment method without creating provider sessions.
/// </summary>
public interface IPaymentProviderRoutingPolicyResolver
{
    /// <summary>
    /// Resolves the configured provider route for the supplied payment method request.
    /// </summary>
    /// <param name="request">The route resolution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deterministic route resolution result.</returns>
    Task<ResolvePaymentProviderRouteResponse> ResolveAsync(
        ResolvePaymentProviderRouteRequest request,
        CancellationToken cancellationToken);
}
