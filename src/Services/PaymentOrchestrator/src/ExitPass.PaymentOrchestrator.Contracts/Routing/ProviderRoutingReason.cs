namespace ExitPass.PaymentOrchestrator.Contracts.Routing;

/// <summary>
/// Deterministic routing result reason codes.
/// </summary>
public static class ProviderRoutingReason
{
    /// <summary>
    /// The route selected the caller's valid preferred provider.
    /// </summary>
    public const string PreferredProviderSelected = "PREFERRED_PROVIDER_SELECTED";

    /// <summary>
    /// The route selected the configured primary provider.
    /// </summary>
    public const string PrimaryProviderSelected = "PRIMARY_PROVIDER_SELECTED";

    /// <summary>
    /// The route selected fallback because the primary provider is disabled.
    /// </summary>
    public const string FallbackProviderSelectedPrimaryDisabled = "FALLBACK_PROVIDER_SELECTED_PRIMARY_DISABLED";

    /// <summary>
    /// The preferred provider is not configured for the payment method.
    /// </summary>
    public const string PreferredProviderUnsupported = "PREFERRED_PROVIDER_UNSUPPORTED";

    /// <summary>
    /// The preferred provider is configured but disabled for the payment method.
    /// </summary>
    public const string PreferredProviderDisabled = "PREFERRED_PROVIDER_DISABLED";

    /// <summary>
    /// No enabled route exists for the payment method.
    /// </summary>
    public const string NoRoute = "NO_ROUTE";

    /// <summary>
    /// No enabled route exists for the requested currency.
    /// </summary>
    public const string CurrencyUnsupported = "CURRENCY_UNSUPPORTED";

    /// <summary>
    /// The requested amount is outside the configured route bounds.
    /// </summary>
    public const string AmountOutsidePolicyBounds = "AMOUNT_OUTSIDE_POLICY_BOUNDS";
}
