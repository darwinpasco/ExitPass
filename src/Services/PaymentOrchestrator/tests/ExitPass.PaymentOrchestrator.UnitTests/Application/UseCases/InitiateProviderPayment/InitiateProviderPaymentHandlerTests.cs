using Microsoft.Extensions.Logging.Abstractions;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using NSubstitute;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.InitiateProviderPayment;

/// <summary>
/// Unit tests for <see cref="InitiateProviderPaymentHandler"/>.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider session creation must remain traceable to a single PaymentAttempt.
/// - POA may initiate provider flows but may not finalize PaymentAttempt state.
/// </summary>
public sealed class InitiateProviderPaymentHandlerTests
{
    /// <summary>
    /// Verifies that the handler returns a redirect handoff for a PayMongo Checkout Session flow.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ReturnsRedirectHandoff_ForPayMongoCheckout()
    {
        var registry = Substitute.For<IPaymentProviderRegistry>();
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = Substitute.For<IPaymentProviderAdapter>();

        var paymentAttemptId = Guid.NewGuid();

        registry.GetRequired("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION").Returns(adapter);

        adapter.CreatePaymentSessionAsync(
                Arg.Any<CreateProviderPaymentSessionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateProviderPaymentSessionResult(
                "cs_test_123",
                "cs_test_123",
                "PENDING_PROVIDER",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://checkout.paymongo.test/session",
                    "GET",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(30)),
                DateTimeOffset.UtcNow.AddMinutes(30),
                "{\"data\":{}}"));

        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var request = new InitiateProviderPaymentRequest(
            paymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            15000,
            "PHP",
            "ExitPass parking payment",
            Guid.NewGuid().ToString("N"),
            "https://example.test/success",
            "https://example.test/failure",
            "https://example.test/cancel",
            "https://example.test/webhook",
            new Dictionary<string, string> { ["payment_attempt_id"] = paymentAttemptId.ToString() });

        var response = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(paymentAttemptId, response.PaymentAttemptId);
        Assert.Equal("PAYMONGO", response.ProviderCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", response.ProviderProduct);
        Assert.Equal("cs_test_123", response.ProviderSessionId);
        Assert.Equal(ProviderHandoffType.Redirect, response.ProviderHandoff.Type);
        Assert.Equal("https://checkout.paymongo.test/session", response.ProviderHandoff.RedirectUrl);
    }

    /// <summary>
    /// Verifies that the handler persists provider session evidence after successful provider session creation.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PersistsProviderSessionRecord()
    {
        var registry = Substitute.For<IPaymentProviderRegistry>();
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = Substitute.For<IPaymentProviderAdapter>();

        var paymentAttemptId = Guid.NewGuid();

        registry.GetRequired("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION").Returns(adapter);

        adapter.CreatePaymentSessionAsync(
                Arg.Any<CreateProviderPaymentSessionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateProviderPaymentSessionResult(
                "cs_test_456",
                "cs_test_456",
                "PENDING_PROVIDER",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://checkout.paymongo.test/another-session",
                    "GET",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(30)),
                DateTimeOffset.UtcNow.AddMinutes(30),
                "{\"data\":{\"id\":\"cs_test_456\"}}"));

        var handler = new InitiateProviderPaymentHandler(
            NullLogger<InitiateProviderPaymentHandler>.Instance,
            registry,
            repository);

        var request = new InitiateProviderPaymentRequest(
            paymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            25000,
            "PHP",
            "ExitPass parking payment",
            Guid.NewGuid().ToString("N"),
            "https://example.test/success",
            "https://example.test/failure",
            "https://example.test/cancel",
            "https://example.test/webhook",
            new Dictionary<string, string> { ["payment_attempt_id"] = paymentAttemptId.ToString() });

        await handler.HandleAsync(request, CancellationToken.None);

        await repository.Received(1).AddAsync(
            Arg.Is<ProviderSessionRecord>(x =>
                x.PaymentAttemptId == paymentAttemptId &&
                x.ProviderCode == "PAYMONGO" &&
                x.ProviderProduct == "PAYMONGO_CHECKOUT_SESSION" &&
                x.ProviderSessionId == "cs_test_456" &&
                x.SessionStatus == "PENDING_PROVIDER"),
            Arg.Any<CancellationToken>());
    }
}
