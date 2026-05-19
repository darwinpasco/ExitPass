namespace ExitPass.PaymentOrchestrator.Contracts.Routing;

/// <summary>
/// Request to resolve the payment provider route for a customer-selected payment method.
/// </summary>
/// <param name="SiteId">Optional site identifier for future site-specific routing.</param>
/// <param name="SiteGroupId">Optional site group identifier for future group routing.</param>
/// <param name="PaymentMethod">Customer-selected payment method code.</param>
/// <param name="AmountMinorUnits">Payment amount in minor currency units.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="PreferredProviderCode">Optional caller-requested provider override.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record ResolvePaymentProviderRouteRequest(
    Guid? SiteId,
    Guid? SiteGroupId,
    string PaymentMethod,
    long AmountMinorUnits,
    string Currency,
    string? PreferredProviderCode,
    Guid CorrelationId);
