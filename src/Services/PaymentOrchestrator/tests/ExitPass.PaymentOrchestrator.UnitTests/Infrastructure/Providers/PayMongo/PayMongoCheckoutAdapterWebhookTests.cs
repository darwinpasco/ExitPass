using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Providers.PayMongo;

/// <summary>
/// Unit tests for PayMongo webhook verification and provider-neutral outcome canonicalization.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Provider callbacks must be authentic before entering platform payment evidence handling.
/// - Provider-specific states must be canonicalized before crossing the POA boundary.
/// - Failed, cancelled, or expired provider outcomes must not be represented as successful payment finality.
/// </summary>
public sealed class PayMongoCheckoutAdapterWebhookTests
{
    private const string WebhookSecretKey = "whsec_unit_test_only";

    /// <summary>
    /// Verifies that a signed PayMongo paid checkout-session callback is accepted
    /// and canonicalized as a successful terminal provider outcome.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenCheckoutSessionPaidSignatureIsValid_ReturnsSucceededOutcome()
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload("evt_paid_001", "checkout_session.payment.paid", "cs_paid_001");

        var result = await adapter.VerifyWebhookAsync(CreateSignedRequest(payload), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal("evt_paid_001", result.EventId);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Succeeded, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"), result.PaymentAttemptId);
    }

    /// <summary>
    /// Verifies that a signed PayMongo failure callback is accepted as verified evidence
    /// but canonicalized as failed, not successful, finality.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenPaymentFailedSignatureIsValid_ReturnsFailedOutcomeWithoutSuccess()
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload("evt_failed_001", "payment.failed", "pay_failed_001");

        var result = await adapter.VerifyWebhookAsync(CreateSignedRequest(payload), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Failed, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that invalid PayMongo signatures fail closed before the callback is
    /// treated as verified provider evidence.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenSignatureIsInvalid_ReturnsNotAuthentic()
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload("evt_invalid_signature_001", "checkout_session.payment.paid", "cs_invalid_001");

        var request = new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>
            {
                ["Paymongo-Signature"] = ComputePayMongoSignatureHeader(payload, "wrong_secret")
            },
            RawBody: payload);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
        Assert.Equal("PAYMONGO_WEBHOOK_INVALID_SIGNATURE", result.EventId);
    }

    private static PayMongoCheckoutAdapter CreateAdapter()
    {
        var options = Options.Create(new PayMongoOptions
        {
            BaseUrl = "https://api.paymongo.test",
            SecretKey = "sk_test_unit",
            PublicKey = "pk_test_unit",
            WebhookSecretKey = WebhookSecretKey,
            IsLiveMode = false
        });

        return new PayMongoCheckoutAdapter(
            new PayMongoClient(new HttpClient(new StubHttpMessageHandler()), options),
            options);
    }

    private static ProviderWebhookRequest CreateSignedRequest(string payload)
    {
        return new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>
            {
                ["Paymongo-Signature"] = ComputePayMongoSignatureHeader(payload, WebhookSecretKey)
            },
            RawBody: payload);
    }

    private static string ComputePayMongoSignatureHeader(string payload, string secretKey)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signedPayload = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return $"t={timestamp},te={signature}";
    }

    private static string BuildWebhookPayload(
        string eventId,
        string eventType,
        string providerReference)
    {
        var body = new
        {
            data = new
            {
                id = eventId,
                type = "event",
                attributes = new
                {
                    type = eventType,
                    created_at = 1_775_470_400,
                    data = new
                    {
                        id = providerReference,
                        type = "payment",
                        attributes = new
                        {
                            amount = 5000,
                            currency = "PHP",
                            checkout_session_id = "cs_293285f3347f5496c48332d8",
                            metadata = new Dictionary<string, string>
                            {
                                ["payment_attempt_id"] = "be88ff8e-90a7-45a7-bb7d-3505cfce9076",
                                ["parking_session_id"] = "93e97f33-5849-4b9f-a83f-1080820103d8",
                                ["requested_by_user_id"] = "9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41"
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Webhook verification must not call PayMongo HTTP APIs.");
        }
    }
}
