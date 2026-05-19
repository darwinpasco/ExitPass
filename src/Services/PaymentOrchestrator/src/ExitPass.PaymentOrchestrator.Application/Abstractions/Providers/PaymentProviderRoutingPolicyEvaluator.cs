using ExitPass.PaymentOrchestrator.Contracts.Routing;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Evaluates payment provider routing policies without calling live payment providers.
/// </summary>
public sealed class PaymentProviderRoutingPolicyEvaluator
{
    /// <summary>
    /// Resolves a route from the supplied policy rows.
    /// </summary>
    /// <param name="request">The route request.</param>
    /// <param name="policies">Candidate routing policy rows.</param>
    /// <returns>The route resolution result.</returns>
    public ResolvePaymentProviderRouteResponse Resolve(
        ResolvePaymentProviderRouteRequest request,
        IReadOnlyCollection<PaymentProviderRoutingPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policies);

        var paymentMethod = Normalize(request.PaymentMethod);
        var currency = Normalize(request.Currency);
        var preferredProviderCode = NormalizeNullable(request.PreferredProviderCode);

        var methodPolicies = policies
            .Where(policy => string.Equals(Normalize(policy.PaymentMethod), paymentMethod, StringComparison.Ordinal))
            .ToArray();

        if (methodPolicies.Length == 0)
        {
            return NoRoute(request, paymentMethod, ProviderRoutingReason.NoRoute);
        }

        var currencyPolicies = methodPolicies
            .Where(policy => string.Equals(Normalize(policy.Currency), currency, StringComparison.Ordinal))
            .ToArray();

        if (currencyPolicies.Length == 0)
        {
            return NoRoute(request, paymentMethod, ProviderRoutingReason.CurrencyUnsupported);
        }

        var amountPolicies = currencyPolicies
            .Where(policy => IsAmountWithinBounds(request.AmountMinorUnits, policy))
            .ToArray();

        if (amountPolicies.Length == 0)
        {
            return NoRoute(request, paymentMethod, ProviderRoutingReason.AmountOutsidePolicyBounds);
        }

        var policy = amountPolicies.FirstOrDefault(policy => policy.IsEnabled);
        if (policy is null)
        {
            return NoRoute(request, paymentMethod, ProviderRoutingReason.NoRoute);
        }

        if (!string.IsNullOrWhiteSpace(preferredProviderCode))
        {
            return ResolvePreferredProvider(request, policy, paymentMethod, preferredProviderCode);
        }

        if (policy.PrimaryProviderEnabled)
        {
            return Routed(
                request,
                policy,
                Normalize(policy.PrimaryProviderCode),
                ProviderRoutingReason.PrimaryProviderSelected);
        }

        if (!string.IsNullOrWhiteSpace(policy.FallbackProviderCode) && policy.FallbackProviderEnabled)
        {
            return Routed(
                request,
                policy,
                Normalize(policy.FallbackProviderCode),
                ProviderRoutingReason.FallbackProviderSelectedPrimaryDisabled);
        }

        return NoRoute(request, paymentMethod, ProviderRoutingReason.NoRoute, policy.RoutingPolicyId);
    }

    private static ResolvePaymentProviderRouteResponse ResolvePreferredProvider(
        ResolvePaymentProviderRouteRequest request,
        PaymentProviderRoutingPolicy policy,
        string paymentMethod,
        string preferredProviderCode)
    {
        var primaryProviderCode = Normalize(policy.PrimaryProviderCode);
        var fallbackProviderCode = NormalizeNullable(policy.FallbackProviderCode);

        if (string.Equals(preferredProviderCode, primaryProviderCode, StringComparison.Ordinal))
        {
            return policy.PrimaryProviderEnabled
                ? Routed(request, policy, primaryProviderCode, ProviderRoutingReason.PreferredProviderSelected)
                : NoRoute(request, paymentMethod, ProviderRoutingReason.PreferredProviderDisabled, policy.RoutingPolicyId);
        }

        if (string.Equals(preferredProviderCode, fallbackProviderCode, StringComparison.Ordinal))
        {
            return policy.FallbackProviderEnabled
                ? Routed(request, policy, preferredProviderCode, ProviderRoutingReason.PreferredProviderSelected)
                : NoRoute(request, paymentMethod, ProviderRoutingReason.PreferredProviderDisabled, policy.RoutingPolicyId);
        }

        return NoRoute(request, paymentMethod, ProviderRoutingReason.PreferredProviderUnsupported, policy.RoutingPolicyId);
    }

    private static ResolvePaymentProviderRouteResponse Routed(
        ResolvePaymentProviderRouteRequest request,
        PaymentProviderRoutingPolicy policy,
        string selectedProviderCode,
        string reason)
    {
        return new ResolvePaymentProviderRouteResponse(
            IsRouted: true,
            PaymentMethod: Normalize(policy.PaymentMethod),
            SelectedProviderCode: selectedProviderCode,
            FallbackProviderCode: policy.FallbackProviderEnabled ? NormalizeNullable(policy.FallbackProviderCode) : null,
            RoutingPolicyId: policy.RoutingPolicyId,
            RoutingReason: reason,
            IsFallbackEligible: policy.FallbackProviderEnabled && !string.IsNullOrWhiteSpace(policy.FallbackProviderCode),
            CorrelationId: request.CorrelationId,
            ErrorCode: null);
    }

    private static ResolvePaymentProviderRouteResponse NoRoute(
        ResolvePaymentProviderRouteRequest request,
        string paymentMethod,
        string reason,
        Guid? policyId = null)
    {
        return new ResolvePaymentProviderRouteResponse(
            IsRouted: false,
            PaymentMethod: paymentMethod,
            SelectedProviderCode: null,
            FallbackProviderCode: null,
            RoutingPolicyId: policyId,
            RoutingReason: reason,
            IsFallbackEligible: false,
            CorrelationId: request.CorrelationId,
            ErrorCode: reason);
    }

    private static bool IsAmountWithinBounds(long amountMinorUnits, PaymentProviderRoutingPolicy policy)
    {
        if (policy.MinAmountMinorUnits.HasValue && amountMinorUnits < policy.MinAmountMinorUnits.Value)
        {
            return false;
        }

        if (policy.MaxAmountMinorUnits.HasValue && amountMinorUnits > policy.MaxAmountMinorUnits.Value)
        {
            return false;
        }

        return true;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
}
