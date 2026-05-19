using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ExitPass.PaymentOrchestrator.ContractTests.ProviderWebhooks;

/// <summary>
/// Contract tests for the Payment Orchestrator PayMongo provider-webhook API.
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
/// - Provider callbacks must be authenticated before being accepted.
/// - POA reports verified payment evidence and does not claim Central PMS finality.
/// - Duplicate provider events are deterministic and idempotent.
/// - Error responses use the v1.2 provider-webhook error envelope.
/// </summary>
public sealed class PayMongoWebhookContractTests
    : IClassFixture<PaymentOrchestratorContractWebApplicationFactory>
{
    private const string WebhookRoute = "/v1/provider/paymongo/webhooks";

    private readonly PaymentOrchestratorContractWebApplicationFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoWebhookContractTests"/> class.
    /// </summary>
    /// <param name="factory">The contract-test web application factory.</param>
    public PayMongoWebhookContractTests(PaymentOrchestratorContractWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _factory.ResetState();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Verifies that a valid signed provider success callback is accepted and reported
    /// as provider-neutral evidence without POA claiming Central PMS finality.
    /// </summary>
    [Fact]
    public async Task PayMongoWebhook_WhenSuccessCallbackIsAuthentic_ReturnsOkAndReportsProviderNeutralOutcome()
    {
        var payload = BuildCheckoutPayload(
            eventId: "evt_contract_success_001",
            eventType: "checkout_session.payment.paid",
            providerReference: "cs_contract_success_001");

        using var request = CreateSignedWebhookRequest(payload);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("finalAttemptStatus", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exitAuthorization", body, StringComparison.OrdinalIgnoreCase);

        var report = Assert.Single(_factory.ReportedOutcomes);
        Assert.Equal("SUCCEEDED", report.CanonicalStatus);
        Assert.True(report.IsTerminal);
        Assert.True(report.IsSuccess);
        Assert.Equal("PAYMONGO", report.ProviderCode);
        Assert.Equal("evt_contract_success_001", report.EventId);
    }

    /// <summary>
    /// Verifies that invalid PayMongo signatures are rejected deterministically.
    /// </summary>
    [Fact]
    public async Task PayMongoWebhook_WhenSignatureIsInvalid_ReturnsUnauthorizedErrorEnvelope()
    {
        var correlationId = Guid.NewGuid();
        var payload = BuildCheckoutPayload(
            eventId: "evt_contract_bad_signature_001",
            eventType: "checkout_session.payment.paid",
            providerReference: "cs_contract_bad_signature_001");

        using var request = CreateSignedWebhookRequest(payload, signingSecret: $"wrong_{Guid.NewGuid():N}");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.ReportedOutcomes);

        var error = await ReadJsonAsync(response);
        Assert.Equal("WEBHOOK_NOT_AUTHENTIC", error.RootElement.GetProperty("error_code").GetString());
        Assert.Equal(correlationId.ToString(), error.RootElement.GetProperty("correlation_id").GetString());
        Assert.False(error.RootElement.GetProperty("retryable").GetBoolean());
    }

    /// <summary>
    /// Verifies that an authentic callback missing required v1.2 internal metadata
    /// returns a deterministic bad-request envelope.
    /// </summary>
    [Fact]
    public async Task PayMongoWebhook_WhenRequiredMetadataIsMissing_ReturnsBadRequestErrorEnvelope()
    {
        var payload = BuildCheckoutPayload(
            eventId: "evt_contract_missing_metadata_001",
            eventType: "checkout_session.payment.paid",
            providerReference: "cs_contract_missing_metadata_001",
            includeParkingSessionId: false);

        using var request = CreateSignedWebhookRequest(payload);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.ReportedOutcomes);

        var error = await ReadJsonAsync(response);
        Assert.Equal("WEBHOOK_MISSING_PARKING_SESSION_ID", error.RootElement.GetProperty("error_code").GetString());
        Assert.Equal("Required webhook metadata field 'parking_session_id' is missing or invalid.", error.RootElement.GetProperty("message").GetString());
        Assert.False(error.RootElement.GetProperty("retryable").GetBoolean());
    }

    /// <summary>
    /// Verifies that replayed provider event identifiers are acknowledged idempotently
    /// without reporting duplicate verified outcomes.
    /// </summary>
    [Fact]
    public async Task PayMongoWebhook_WhenProviderEventIsReplayed_ReturnsOkWithoutDuplicateOutcomeReport()
    {
        var payload = BuildCheckoutPayload(
            eventId: "evt_contract_duplicate_001",
            eventType: "checkout_session.payment.paid",
            providerReference: "cs_contract_duplicate_001");

        using var firstRequest = CreateSignedWebhookRequest(payload);
        using var firstResponse = await _client.SendAsync(firstRequest);

        using var replayRequest = CreateSignedWebhookRequest(payload);
        using var replayResponse = await _client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Single(_factory.ReportedOutcomes);
    }

    /// <summary>
    /// Verifies that provider terminal failure-like outcomes are accepted as evidence
    /// but never reported as successful payment finality by POA.
    /// </summary>
    /// <param name="eventType">The PayMongo event type.</param>
    /// <param name="expectedCanonicalStatus">The expected provider-neutral status.</param>
    [Theory]
    [InlineData("payment.failed", "FAILED")]
    [InlineData("payment.cancelled", "CANCELLED")]
    [InlineData("payment.expired", "EXPIRED")]
    public async Task PayMongoWebhook_WhenTerminalNonSuccessOutcomeIsAuthentic_DoesNotClaimSuccess(
        string eventType,
        string expectedCanonicalStatus)
    {
        var payload = BuildCheckoutPayload(
            eventId: $"evt_contract_{expectedCanonicalStatus.ToLowerInvariant()}_001",
            eventType: eventType,
            providerReference: $"pay_contract_{expectedCanonicalStatus.ToLowerInvariant()}_001");

        using var request = CreateSignedWebhookRequest(payload);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = Assert.Single(_factory.ReportedOutcomes);
        Assert.Equal(expectedCanonicalStatus, report.CanonicalStatus);
        Assert.True(report.IsTerminal);
        Assert.False(report.IsSuccess);
    }

    private HttpRequestMessage CreateSignedWebhookRequest(string payload, string? signingSecret = null)
    {
        var secret = signingSecret ?? _factory.PayMongoWebhookSecretKey;

        var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("Paymongo-Signature", ComputePayMongoSignatureHeader(payload, secret));
        return request;
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

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static string BuildCheckoutPayload(
        string eventId,
        string eventType,
        string providerReference,
        bool includeParkingSessionId = true,
        bool includeRequestedByUserId = true)
    {
        var metadata = new Dictionary<string, string>
        {
            ["payment_attempt_id"] = "be88ff8e-90a7-45a7-bb7d-3505cfce9076",
            ["correlation_id"] = "6de95bb4-8f5a-4170-9184-e8eb4cb15c57"
        };

        if (includeParkingSessionId)
        {
            metadata["parking_session_id"] = "93e97f33-5849-4b9f-a83f-1080820103d8";
        }

        if (includeRequestedByUserId)
        {
            metadata["requested_by_user_id"] = "9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41";
        }

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
                            metadata
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }
}
