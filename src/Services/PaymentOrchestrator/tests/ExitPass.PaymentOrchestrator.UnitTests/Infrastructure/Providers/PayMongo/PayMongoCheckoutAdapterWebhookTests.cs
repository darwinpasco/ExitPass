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

    [Fact]
    public async Task VerifyWebhookAsync_WhenCheckoutSessionPayloadCarriesPaymentsArray_UsesPaymentEvidence()
    {
        var adapter = CreateAdapter();
        var payload = BuildCheckoutSessionWebhookPayloadWithPaymentsArray();

        var result = await adapter.VerifyWebhookAsync(CreateSignedRequest(payload), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Succeeded, result.CanonicalStatus);
        Assert.Equal("cs_paid_with_array_001", result.ProviderSessionId);
        Assert.Equal("pay_paid_with_array_001", result.ProviderReference);
        Assert.Equal(10000, result.AmountMinor);
        Assert.Equal("PHP", result.Currency);
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

    /// <summary>
    /// Verifies unsigned PayMongo callbacks fail closed before entering verified evidence handling.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenSignatureIsMissing_ReturnsNotAuthentic()
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload("evt_missing_signature_001", "checkout_session.payment.paid", "cs_missing_signature_001");

        var result = await adapter.VerifyWebhookAsync(
            new ProviderWebhookRequest(new Dictionary<string, string>(), payload),
            CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
        Assert.Equal("PAYMONGO_WEBHOOK_MISSING_SIGNATURE", result.EventId);
    }

    /// <summary>
    /// Verifies a malformed PayMongo callback fails closed and is not treated as verified provider evidence.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenPayloadIsMalformed_ReturnsNotAuthentic()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(
            new ProviderWebhookRequest(
                new Dictionary<string, string>
                {
                    ["Paymongo-Signature"] = "t=1775470400,te=not-a-real-signature"
                },
                "{ this is not valid json"),
            CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
        Assert.Equal("PAYMONGO_WEBHOOK_INVALID_JSON", result.EventId);
    }

    /// <summary>
    /// Verifies PayMongo callbacks without a signature timestamp fail closed as unverifiable.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenSignatureTimestampIsMissing_ReturnsNotAuthentic()
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload("evt_missing_timestamp_001", "checkout_session.payment.paid", "cs_missing_timestamp_001");

        var result = await adapter.VerifyWebhookAsync(
            new ProviderWebhookRequest(
                new Dictionary<string, string>
                {
                    ["Paymongo-Signature"] = "te=not-a-real-signature"
                },
                payload),
            CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
        Assert.Equal("PAYMONGO_WEBHOOK_MISSING_SIGNATURE_TIMESTAMP", result.EventId);
    }

    /// <summary>
    /// Verifies stale signed callbacks fail closed as replay-window violations.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenSignatureTimestampIsOutsideReplayWindow_ReturnsNotAuthentic()
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload("evt_stale_signature_001", "checkout_session.payment.paid", "cs_stale_001");

        var request = new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>
            {
                ["Paymongo-Signature"] = ComputePayMongoSignatureHeader(
                    payload,
                    WebhookSecretKey,
                    DateTimeOffset.UtcNow.AddMinutes(-10))
            },
            RawBody: payload);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.Equal("PAYMONGO_WEBHOOK_SIGNATURE_TIMESTAMP_OUTSIDE_WINDOW", result.EventId);
    }

    /// <summary>
    /// Verifies live-mode signature validation uses PayMongo's live signature component.
    /// </summary>
    [Fact]
    public async Task VerifyWebhookAsync_WhenLiveModeUsesLiveSignature_ReturnsSucceededOutcome()
    {
        var adapter = CreateAdapter(isLiveMode: true);
        var payload = BuildWebhookPayload("evt_live_paid_001", "payment.succeeded", "pay_live_paid_001");

        var result = await adapter.VerifyWebhookAsync(
            new ProviderWebhookRequest(
                new Dictionary<string, string>
                {
                    ["Paymongo-Signature"] = ComputePayMongoSignatureHeader(payload, WebhookSecretKey, signatureKey: "li")
                },
                payload),
            CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Succeeded, result.CanonicalStatus);
    }

    /// <summary>
    /// Verifies cancelled and expired PayMongo events are terminal but not successful.
    /// </summary>
    [Theory]
    [InlineData("checkout_session.expired", CanonicalPaymentOutcomeStatus.Expired)]
    [InlineData("checkout_session.cancelled", CanonicalPaymentOutcomeStatus.Cancelled)]
    [InlineData("checkout_session.payment.failed", CanonicalPaymentOutcomeStatus.Failed)]
    [InlineData("payment.canceled", CanonicalPaymentOutcomeStatus.Cancelled)]
    public async Task VerifyWebhookAsync_WhenTerminalNonSuccessEventIsSigned_ReturnsTerminalNonSuccess(
        string eventType,
        CanonicalPaymentOutcomeStatus expectedStatus)
    {
        var adapter = CreateAdapter();
        var payload = BuildWebhookPayload($"evt_{eventType.Replace('.', '_')}", eventType, "pay_terminal_non_success");

        var result = await adapter.VerifyWebhookAsync(CreateSignedRequest(payload), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(expectedStatus, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    private static PayMongoCheckoutAdapter CreateAdapter(bool isLiveMode = false)
    {
        var options = Options.Create(new PayMongoOptions
        {
            BaseUrl = "https://api.paymongo.test",
            SecretKey = "sk_test_unit",
            PublicKey = "pk_test_unit",
            WebhookSecretKey = WebhookSecretKey,
            IsLiveMode = isLiveMode
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

    private static string ComputePayMongoSignatureHeader(
        string payload,
        string secretKey,
        DateTimeOffset? timestamp = null,
        string signatureKey = "te")
    {
        var timestampText = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var signedPayload = $"{timestampText}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return $"t={timestampText},{signatureKey}={signature}";
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

    private static string BuildCheckoutSessionWebhookPayloadWithPaymentsArray()
    {
        var body = new
        {
            data = new
            {
                id = "evt_paid_array_001",
                type = "event",
                attributes = new
                {
                    type = "checkout_session.paid",
                    created_at = 1_775_470_400,
                    data = new
                    {
                        id = "cs_paid_with_array_001",
                        type = "checkout_session",
                        attributes = new
                        {
                            payments = new[]
                            {
                                new
                                {
                                    id = "pay_paid_with_array_001",
                                    type = "payment",
                                    attributes = new
                                    {
                                        amount = 10000,
                                        currency = "PHP"
                                    }
                                }
                            },
                            metadata = new Dictionary<string, string>
                            {
                                ["payment_attempt_id"] = "be88ff8e-90a7-45a7-bb7d-3505cfce9076",
                                ["parking_session_id"] = "93e97f33-5849-4b9f-a83f-1080820103d8",
                                ["correlation_id"] = "6de95bb4-8f5a-4170-9184-e8eb4cb15c57"
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
