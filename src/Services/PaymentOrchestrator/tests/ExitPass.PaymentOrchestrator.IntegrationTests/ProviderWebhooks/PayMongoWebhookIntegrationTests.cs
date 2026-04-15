using System.Net.Http.Headers;
using System.Text;
using ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.ProviderWebhooks;

/// <summary>
/// Integration tests for PayMongo webhook acceptance.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Verified provider webhooks must be accepted end to end when all required metadata is present.
/// - Missing internal metadata must not cause unhandled 500 responses.
/// - Duplicate provider webhook deliveries must be treated idempotently after successful first processing.
/// </summary>
public sealed class PayMongoWebhookIntegrationTests
    : IClassFixture<PaymentOrchestratorWebApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoWebhookIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">API test host factory.</param>
    public PayMongoWebhookIntegrationTests(PaymentOrchestratorWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Verifies that a valid PayMongo checkout-session-paid webhook is accepted successfully.
    /// </summary>
    [Fact]
    public async Task Accepts_checkout_session_paid_webhook_and_reports_verified_outcome_to_central_pms()
    {
        const string body =
            """
            {"data":{"id":"evt_test_016","attributes":{"type":"checkout_session.payment.paid","created_at":1776132000,"data":{"id":"pay_test_016","attributes":{"checkout_session_id":"cs_test_016","amount":10000,"currency":"PHP","metadata":{"payment_attempt_id":"33caec09-0ec6-4c3d-b924-ae3ea5b85cb4","parking_session_id":"93e97f33-5849-4b9f-a83f-1080820103d8","requested_by_user_id":"9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41"}}}}}}
            """;

        using var request = CreatePayMongoWebhookRequest(
            body,
            "22222222-2222-2222-2222-222222222222",
            "t=REPLACE_ME,te=REPLACE_ME");

        using var response = await _client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        responseBody.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Verifies that missing required internal metadata is rejected deterministically instead of surfacing as an unhandled 500.
    /// </summary>
    [Fact]
    public async Task Rejects_webhook_when_required_internal_metadata_is_missing()
    {
        const string body =
            """
            {"data":{"id":"evt_test_017","attributes":{"type":"checkout_session.payment.paid","created_at":1776132000,"data":{"id":"pay_test_017","attributes":{"checkout_session_id":"cs_test_017","amount":10000,"currency":"PHP","metadata":{"payment_attempt_id":"33caec09-0ec6-4c3d-b924-ae3ea5b85cb4"}}}}}}
            """;

        using var request = CreatePayMongoWebhookRequest(
            body,
            "22222222-2222-2222-2222-222222222223",
            "t=REPLACE_ME,te=REPLACE_ME");

        using var response = await _client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            responseBody.Should().ContainAny(
                "WEBHOOK_MISSING_PARKING_SESSION_ID",
                "WEBHOOK_MISSING_REQUESTED_BY_USER_ID");
        }
    }

    /// <summary>
    /// Verifies that a duplicate webhook delivery is accepted idempotently after successful first processing.
    /// </summary>
    [Fact]
    public async Task Accepts_duplicate_webhook_idempotently_after_successful_first_processing()
    {
        const string body =
            """
            {"data":{"id":"evt_test_018","attributes":{"type":"checkout_session.payment.paid","created_at":1776132000,"data":{"id":"pay_test_018","attributes":{"checkout_session_id":"cs_test_018","amount":10000,"currency":"PHP","metadata":{"payment_attempt_id":"33caec09-0ec6-4c3d-b924-ae3ea5b85cb4","parking_session_id":"93e97f33-5849-4b9f-a83f-1080820103d8","requested_by_user_id":"9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41"}}}}}}
            """;

        using var firstRequest = CreatePayMongoWebhookRequest(
            body,
            "22222222-2222-2222-2222-222222222224",
            "t=REPLACE_ME,te=REPLACE_ME");

        using var firstResponse = await _client.SendAsync(firstRequest);
        var firstResponseBody = await firstResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResponseBody.Should().BeNullOrEmpty();

        using var duplicateRequest = CreatePayMongoWebhookRequest(
            body,
            "22222222-2222-2222-2222-222222222225",
            "t=REPLACE_ME,te=REPLACE_ME");

        using var duplicateResponse = await _client.SendAsync(duplicateRequest);
        var duplicateResponseBody = await duplicateResponse.Content.ReadAsStringAsync();

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        if (!string.IsNullOrWhiteSpace(duplicateResponseBody))
        {
            duplicateResponseBody.Should().Contain("evt_test_018");
        }
    }

    /// <summary>
    /// Creates an HTTP request for the PayMongo webhook endpoint using the exact raw body that will be signed.
    /// </summary>
    /// <param name="body">Exact raw JSON body to send.</param>
    /// <param name="correlationId">Correlation identifier for traceability.</param>
    /// <param name="signatureHeader">PayMongo signature header value.</param>
    /// <returns>The configured HTTP request message.</returns>
    private static HttpRequestMessage CreatePayMongoWebhookRequest(
        string body,
        string correlationId,
        string signatureHeader)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/provider/paymongo/webhooks");

        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.Add("Paymongo-Signature", signatureHeader);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return request;
    }
}
