namespace ExitPass.PaymentOrchestrator.Contracts.Routing;

/// <summary>
/// Result of resolving a payment provider route for a payment method.
/// </summary>
/// <param name="IsRouted">Indicates whether a provider route was resolved.</param>
/// <param name="PaymentMethod">Resolved payment method code.</param>
/// <param name="SelectedProviderCode">Selected provider code, when a route exists.</param>
/// <param name="FallbackProviderCode">Configured fallback provider code, when available.</param>
/// <param name="RoutingPolicyId">Identifier of the routing policy used, when available.</param>
/// <param name="RoutingReason">Deterministic routing reason code.</param>
/// <param name="IsFallbackEligible">Indicates whether a fallback provider is configured and enabled.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
/// <param name="ErrorCode">Deterministic error code when no route was selected.</param>
public sealed record ResolvePaymentProviderRouteResponse(
    bool IsRouted,
    string PaymentMethod,
    string? SelectedProviderCode,
    string? FallbackProviderCode,
    Guid? RoutingPolicyId,
    string RoutingReason,
    bool IsFallbackEligible,
    Guid CorrelationId,
    string? ErrorCode);
