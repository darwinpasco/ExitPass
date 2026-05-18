using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Providers;

/// <summary>
/// Unit tests for lazy provider adapter resolution.
/// </summary>
public sealed class PaymentProviderRegistryTests
{
    /// <summary>
    /// Verifies PayMongo routing resolves only the PayMongo adapter even when AUB is registered as fallback.
    /// </summary>
    [Fact]
    public void GetRequired_WhenPayMongoCheckoutSelected_DoesNotInstantiateAubAdapter()
    {
        var aubFactoryCalled = false;
        var payMongoAdapter = new StubPaymentProviderAdapter("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION");
        var registry = new PaymentProviderRegistry(
            new[]
            {
                new PaymentProviderAdapterRegistration(
                    "AUB",
                    "AUB_CARD_CASHIER",
                    _ =>
                    {
                        aubFactoryCalled = true;
                        throw new InvalidOperationException("AUB base URL is required.");
                    }),
                new PaymentProviderAdapterRegistration(
                    "PAYMONGO",
                    "PAYMONGO_CHECKOUT_SESSION",
                    _ => payMongoAdapter)
            },
            EmptyServiceProvider.Instance);

        var resolved = registry.GetRequired("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION");

        Assert.Same(payMongoAdapter, resolved);
        Assert.False(aubFactoryCalled);
    }

    /// <summary>
    /// Verifies AUB configuration failures surface only when AUB is actually selected.
    /// </summary>
    [Fact]
    public void GetRequired_WhenAubSelected_InvokesAubAdapterFactory()
    {
        var registry = new PaymentProviderRegistry(
            new[]
            {
                new PaymentProviderAdapterRegistration(
                    "AUB",
                    "AUB_CARD_CASHIER",
                    _ => throw new InvalidOperationException("AUB base URL is required.")),
                new PaymentProviderAdapterRegistration(
                    "PAYMONGO",
                    "PAYMONGO_CHECKOUT_SESSION",
                    _ => new StubPaymentProviderAdapter("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION"))
            },
            EmptyServiceProvider.Instance);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequired("AUB", "AUB_CARD_CASHIER"));
        Assert.Equal("AUB base URL is required.", exception.Message);
    }

    private sealed class StubPaymentProviderAdapter : IPaymentProviderAdapter
    {
        public StubPaymentProviderAdapter(string providerCode, string providerProduct)
        {
            ProviderCode = providerCode;
            ProviderProduct = providerProduct;
        }

        public string ProviderCode { get; }

        public string ProviderProduct { get; }

        public Task<CreateProviderPaymentSessionResult> CreatePaymentSessionAsync(
            CreateProviderPaymentSessionCommand command,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CreateProviderPaymentSessionResult(
                "session_test",
                "reference_test",
                "PENDING_PROVIDER",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://payments.test/handoff",
                    "GET",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(15)),
                DateTimeOffset.UtcNow.AddMinutes(15),
                "{}"));
        }

        public Task<ProviderWebhookVerificationResult> VerifyWebhookAsync(
            ProviderWebhookRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        private EmptyServiceProvider()
        {
        }

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
