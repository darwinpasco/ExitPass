namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Database-backed payment provider routing policy row projected for route evaluation.
/// </summary>
/// <param name="RoutingPolicyId">Routing policy identifier.</param>
/// <param name="PaymentMethod">Payment method code.</param>
/// <param name="PrimaryProviderCode">Configured primary provider code.</param>
/// <param name="FallbackProviderCode">Configured fallback provider code, when available.</param>
/// <param name="Currency">Supported currency code.</param>
/// <param name="MinAmountMinorUnits">Minimum amount in minor units, when bounded.</param>
/// <param name="MaxAmountMinorUnits">Maximum amount in minor units, when bounded.</param>
/// <param name="IsEnabled">Whether the routing policy is enabled.</param>
/// <param name="PrimaryProviderEnabled">Whether the primary provider is enabled for this method.</param>
/// <param name="FallbackProviderEnabled">Whether the fallback provider is enabled for this method.</param>
public sealed record PaymentProviderRoutingPolicy(
    Guid RoutingPolicyId,
    string PaymentMethod,
    string PrimaryProviderCode,
    string? FallbackProviderCode,
    string Currency,
    long? MinAmountMinorUnits,
    long? MaxAmountMinorUnits,
    bool IsEnabled,
    bool PrimaryProviderEnabled,
    bool FallbackProviderEnabled);
