using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.Providers;

/// <summary>
/// Unit tests for database/config-driven payment provider route evaluation.
/// </summary>
public sealed class PaymentProviderRoutingPolicyEvaluatorTests
{
    private static readonly Guid CorrelationId = Guid.Parse("6de95bb4-8f5a-4170-9184-e8eb4cb15c57");

    /// <summary>
    /// Verifies that QRPh routes to AUB primary with PayMongo fallback from policy data.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPaymentMethodIsQrph_ReturnsAubPrimaryAndPayMongoFallback()
    {
        var result = Resolve(PaymentMethodCode.QrPh);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.Aub, result.SelectedProviderCode);
        Assert.Equal(ProviderCode.PayMongo, result.FallbackProviderCode);
        Assert.Equal(ProviderRoutingReason.PrimaryProviderSelected, result.RoutingReason);
        Assert.True(result.IsFallbackEligible);
    }

    /// <summary>
    /// Verifies that the local/testing QRPh override policy routes QRPh through PayMongo with AUB fallback.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenQrphOverridePolicyIsApplied_ReturnsPayMongoPrimaryAndAubFallback()
    {
        var policies = DefaultPolicies().Select(policy =>
            policy.PaymentMethod == PaymentMethodCode.QrPh
                ? policy with
                {
                    PrimaryProviderCode = ProviderCode.PayMongo,
                    FallbackProviderCode = ProviderCode.Aub,
                    PrimaryProviderEnabled = true,
                    FallbackProviderEnabled = true
                }
                : policy).ToArray();

        var result = new PaymentProviderRoutingPolicyEvaluator().Resolve(
            CreateRequest(PaymentMethodCode.QrPh),
            policies);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
        Assert.Equal(ProviderCode.Aub, result.FallbackProviderCode);
        Assert.Equal(ProviderRoutingReason.PrimaryProviderSelected, result.RoutingReason);
        Assert.True(result.IsFallbackEligible);
    }

    /// <summary>
    /// Verifies that card routes to AUB primary with PayMongo fallback from policy data.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPaymentMethodIsCard_ReturnsAubPrimaryAndPayMongoFallback()
    {
        var result = Resolve(PaymentMethodCode.Card);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.Aub, result.SelectedProviderCode);
        Assert.Equal(ProviderCode.PayMongo, result.FallbackProviderCode);
    }

    /// <summary>
    /// Verifies that GCash routes to PayMongo primary from policy data.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPaymentMethodIsGcash_ReturnsPayMongoPrimary()
    {
        var result = Resolve(PaymentMethodCode.GCash);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
        Assert.Null(result.FallbackProviderCode);
        Assert.False(result.IsFallbackEligible);
    }

    /// <summary>
    /// Verifies that Maya routes to PayMongo primary from policy data.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPaymentMethodIsMaya_ReturnsPayMongoPrimary()
    {
        var result = Resolve(PaymentMethodCode.Maya);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
        Assert.Null(result.FallbackProviderCode);
    }

    /// <summary>
    /// Verifies that a supported preferred provider override is honored.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPreferredProviderIsSupported_ReturnsPreferredProvider()
    {
        var result = Resolve(PaymentMethodCode.QrPh, preferredProviderCode: ProviderCode.PayMongo);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
        Assert.Equal(ProviderRoutingReason.PreferredProviderSelected, result.RoutingReason);
    }

    /// <summary>
    /// Verifies that an unsupported preferred provider returns a deterministic validation error.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPreferredProviderIsUnsupported_ReturnsValidationError()
    {
        var result = Resolve(PaymentMethodCode.GCash, preferredProviderCode: ProviderCode.Aub);

        Assert.False(result.IsRouted);
        Assert.Equal(ProviderRoutingReason.PreferredProviderUnsupported, result.ErrorCode);
        Assert.Null(result.SelectedProviderCode);
    }

    /// <summary>
    /// Verifies that a disabled preferred provider returns a deterministic validation error.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPreferredProviderIsDisabled_ReturnsValidationError()
    {
        var policies = DefaultPolicies().Select(policy =>
            policy.PaymentMethod == PaymentMethodCode.QrPh
                ? policy with { FallbackProviderEnabled = false }
                : policy).ToArray();

        var result = new PaymentProviderRoutingPolicyEvaluator().Resolve(
            CreateRequest(PaymentMethodCode.QrPh, preferredProviderCode: ProviderCode.PayMongo),
            policies);

        Assert.False(result.IsRouted);
        Assert.Equal(ProviderRoutingReason.PreferredProviderDisabled, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that a disabled primary provider falls back when fallback is configured and enabled.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenPrimaryDisabled_ReturnsFallbackProvider()
    {
        var policies = DefaultPolicies().Select(policy =>
            policy.PaymentMethod == PaymentMethodCode.QrPh
                ? policy with { PrimaryProviderEnabled = false }
                : policy).ToArray();

        var result = new PaymentProviderRoutingPolicyEvaluator().Resolve(
            CreateRequest(PaymentMethodCode.QrPh),
            policies);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
        Assert.Equal(ProviderRoutingReason.FallbackProviderSelectedPrimaryDisabled, result.RoutingReason);
    }

    /// <summary>
    /// Verifies that no enabled provider returns a deterministic no-route result.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenNoEnabledProvider_ReturnsNoRouteError()
    {
        var policies = DefaultPolicies().Select(policy =>
            policy.PaymentMethod == PaymentMethodCode.QrPh
                ? policy with { PrimaryProviderEnabled = false, FallbackProviderEnabled = false }
                : policy).ToArray();

        var result = new PaymentProviderRoutingPolicyEvaluator().Resolve(
            CreateRequest(PaymentMethodCode.QrPh),
            policies);

        Assert.False(result.IsRouted);
        Assert.Equal(ProviderRoutingReason.NoRoute, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that unsupported currency returns a deterministic no-route result.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenCurrencyUnsupported_ReturnsNoRouteError()
    {
        var result = Resolve(PaymentMethodCode.QrPh, currency: "USD");

        Assert.False(result.IsRouted);
        Assert.Equal(ProviderRoutingReason.CurrencyUnsupported, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that amount bounds are evaluated from policy data.
    /// </summary>
    [Fact]
    public void ResolveRoute_WhenAmountOutsidePolicyBounds_ReturnsNoRouteError()
    {
        var policies = DefaultPolicies().Select(policy =>
            policy.PaymentMethod == PaymentMethodCode.Card
                ? policy with { MinAmountMinorUnits = 50000 }
                : policy).ToArray();

        var result = new PaymentProviderRoutingPolicyEvaluator().Resolve(
            CreateRequest(PaymentMethodCode.Card),
            policies);

        Assert.False(result.IsRouted);
        Assert.Equal(ProviderRoutingReason.AmountOutsidePolicyBounds, result.ErrorCode);
    }

    /// <summary>
    /// Verifies that route evaluation does not require or call live provider adapters.
    /// </summary>
    [Fact]
    public void ResolveRoute_DoesNotCallLivePaymentProvider()
    {
        var result = Resolve(PaymentMethodCode.Card);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.Aub, result.SelectedProviderCode);
    }

    private static ResolvePaymentProviderRouteResponse Resolve(
        string paymentMethod,
        string currency = "PHP",
        string? preferredProviderCode = null)
    {
        return new PaymentProviderRoutingPolicyEvaluator().Resolve(
            CreateRequest(paymentMethod, currency, preferredProviderCode),
            DefaultPolicies());
    }

    private static ResolvePaymentProviderRouteRequest CreateRequest(
        string paymentMethod,
        string currency = "PHP",
        string? preferredProviderCode = null)
    {
        return new ResolvePaymentProviderRouteRequest(
            SiteId: null,
            SiteGroupId: null,
            PaymentMethod: paymentMethod,
            AmountMinorUnits: 12500,
            Currency: currency,
            PreferredProviderCode: preferredProviderCode,
            CorrelationId);
    }

    private static IReadOnlyCollection<PaymentProviderRoutingPolicy> DefaultPolicies()
    {
        return new[]
        {
            Policy(PaymentMethodCode.QrPh, ProviderCode.Aub, ProviderCode.PayMongo),
            Policy(PaymentMethodCode.Card, ProviderCode.Aub, ProviderCode.PayMongo),
            Policy(PaymentMethodCode.GCash, ProviderCode.PayMongo, null, fallbackProviderEnabled: false),
            Policy(PaymentMethodCode.Maya, ProviderCode.PayMongo, null, fallbackProviderEnabled: false)
        };
    }

    private static PaymentProviderRoutingPolicy Policy(
        string paymentMethod,
        string primaryProviderCode,
        string? fallbackProviderCode,
        bool primaryProviderEnabled = true,
        bool fallbackProviderEnabled = true)
    {
        return new PaymentProviderRoutingPolicy(
            RoutingPolicyId: Guid.NewGuid(),
            PaymentMethod: paymentMethod,
            PrimaryProviderCode: primaryProviderCode,
            FallbackProviderCode: fallbackProviderCode,
            Currency: "PHP",
            MinAmountMinorUnits: null,
            MaxAmountMinorUnits: null,
            IsEnabled: true,
            PrimaryProviderEnabled: primaryProviderEnabled,
            FallbackProviderEnabled: fallbackProviderEnabled);
    }
}
