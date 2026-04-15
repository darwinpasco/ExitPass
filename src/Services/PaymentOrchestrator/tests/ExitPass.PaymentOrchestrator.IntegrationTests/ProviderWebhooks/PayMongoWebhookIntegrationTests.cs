using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;
using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.ProviderWebhooks;

/// <summary>
/// Integration tests for the PayMongo webhook endpoint.
///
/// Implements:
/// - BRD requirement: verified provider webhook intake and verified outcome reporting.
/// - BRD requirement: timeout, retry, and duplicate handling.
/// - SDD section: payment orchestrator inbound provider webhook processing.
///
/// Enforces invariants:
/// - Only structurally valid and authentic provider webhook payloads may reach verified-outcome handling.
/// - Duplicate provider callbacks must be accepted idempotently.
/// - Non-authoritative provider events for the configured rail must not mutate business state.
/// - Missing required internal metadata must fail deterministically and must not surface as unhandled 500 errors.
/// </summary>
public sealed class PayMongoWebhookIntegrationTests
    : IClassFixture<PaymentOrchestratorWebApplicationFactory>
{
    private const string WebhookRoute = "/v1/provider/paymongo/webhooks";
    private const string RequestedByUserId = "9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41";

    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoWebhookIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">The shared POA web application factory.</param>
    public PayMongoWebhookIntegrationTests(PaymentOrchestratorWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Verifies that an authentic PayMongo checkout-session-paid webhook is accepted
    /// when the required internal metadata is complete.
    /// </summary>
    [Fact]
    public async Task Accepts_checkout_session_paid_webhook_when_request_is_authentic_and_metadata_is_complete()
    {
        var payload = BuildValidCheckoutSessionPaidPayload(
            eventId: "evt_test_001",
            checkoutSessionId: "cs_test_001",
            paymentAttemptId: "11111111-1111-1111-1111-111111111111",
            parkingSessionId: "22222222-2222-2222-2222-222222222222",
            requestedByUserId: RequestedByUserId);

        using var request = CreateSignedWebhookRequest(payload);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a duplicate PayMongo webhook is handled idempotently
    /// after the first successful processing attempt.
    /// </summary>
    [Fact]
    public async Task Accepts_duplicate_webhook_idempotently_after_successful_first_processing()
    {
        var payload = BuildValidCheckoutSessionPaidPayload(
            eventId: "evt_test_002",
            checkoutSessionId: "cs_test_002",
            paymentAttemptId: "33333333-3333-3333-3333-333333333333",
            parkingSessionId: "44444444-4444-4444-4444-444444444444",
            requestedByUserId: RequestedByUserId);

        using var firstRequest = CreateSignedWebhookRequest(payload);
        var firstResponse = await _client.SendAsync(firstRequest);

        using var secondRequest = CreateSignedWebhookRequest(payload);
        var secondResponse = await _client.SendAsync(secondRequest);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a PayMongo webhook is rejected with a deterministic bad-request result
    /// when parking_session_id is missing but payment_attempt_id is still present.
    /// </summary>
    [Fact]
    public async Task Rejects_webhook_with_bad_request_when_parking_session_id_is_missing()
    {
        var payload = BuildPayloadMissingParkingSessionId(
            eventId: "evt_test_003",
            checkoutSessionId: "cs_test_003",
            paymentAttemptId: "88888888-8888-8888-8888-888888888888",
            requestedByUserId: RequestedByUserId);

        using var request = CreateSignedWebhookRequest(payload);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("WEBHOOK_MISSING_PARKING_SESSION_ID");
    }

    /// <summary>
    /// Verifies that a PayMongo webhook is rejected with a deterministic bad-request result
    /// when requested_by_user_id is missing but payment_attempt_id is still present.
    /// </summary>
    [Fact]
    public async Task Rejects_webhook_with_bad_request_when_requested_by_user_id_is_missing()
    {
        var payload = BuildPayloadMissingRequestedByUserId(
            eventId: "evt_test_006",
            checkoutSessionId: "cs_test_006",
            paymentAttemptId: "99999999-9999-9999-9999-999999999999",
            parkingSessionId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        using var request = CreateSignedWebhookRequest(payload);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("WEBHOOK_MISSING_REQUESTED_BY_USER_ID");
    }

    /// <summary>
    /// Verifies that a non-authoritative PayMongo event for the checkout-session rail
    /// is safely acknowledged and ignored.
    /// </summary>
    [Fact]
    public async Task Accepts_and_ignores_non_authoritative_payment_paid_event_for_checkout_session_rail()
    {
        var payload = BuildNonAuthoritativePaymentPaidPayload(
            eventId: "evt_test_004",
            paymentId: "pay_test_004",
            paymentAttemptId: "55555555-5555-5555-5555-555555555555");

        using var request = CreateSignedWebhookRequest(payload);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a webhook with an invalid signature is rejected as unauthorized.
    /// </summary>
    [Fact]
    public async Task Rejects_webhook_as_unauthorized_when_signature_is_invalid()
    {
        var payload = BuildValidCheckoutSessionPaidPayload(
            eventId: "evt_test_005",
            checkoutSessionId: "cs_test_005",
            paymentAttemptId: "66666666-6666-6666-6666-666666666666",
            parkingSessionId: "77777777-7777-7777-7777-777777777777",
            requestedByUserId: RequestedByUserId);

        using var request = CreateSignedWebhookRequest(payload, overrideSecretKey: "not-the-real-secret");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("WEBHOOK_NOT_AUTHENTIC");
    }

    /// <summary>
    /// Creates a signed HTTP request for the PayMongo webhook endpoint.
    ///
    /// Implements:
    /// - BRD requirement: authenticated provider webhook intake.
    /// - SDD section: PayMongo webhook authenticity verification.
    ///
    /// Enforces invariant:
    /// - The exact payload sent to the API is the exact payload used for signature generation.
    /// </summary>
    /// <param name="payload">Serialized JSON payload.</param>
    /// <param name="overrideSecretKey">
    /// Optional override secret key used only for negative-path signature tests.
    /// </param>
    /// <returns>The signed HTTP request message.</returns>
    private static HttpRequestMessage CreateSignedWebhookRequest(string payload, string? overrideSecretKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var secretKey = string.IsNullOrWhiteSpace(overrideSecretKey)
            ? GetRequiredPayMongoWebhookSecretKey()
            : overrideSecretKey;

        var signatureHeaderValue = ComputePayMongoSignatureHeader(payload, secretKey);

        var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("paymongo-signature", signatureHeaderValue);
        return request;
    }

    /// <summary>
    /// Resolves the PayMongo webhook secret key from the current process environment first,
    /// then from the local docker .env file when tests are executed from Visual Studio.
    ///
    /// Implements:
    /// - BRD requirement: environment-backed secret management for provider integrations.
    /// - SDD section: payment provider configuration loading.
    ///
    /// Enforces invariant:
    /// - Webhook signing secrets are never hard-coded in test source.
    /// </summary>
    /// <returns>The configured PayMongo webhook secret key.</returns>
    private static string GetRequiredPayMongoWebhookSecretKey()
    {
        const string variableName = "PAYMONGO_WEBHOOK_SECRET_KEY";

        var fromEnvironment = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var fromDotEnv = TryReadValueFromDotEnv(variableName);
        if (!string.IsNullOrWhiteSpace(fromDotEnv))
        {
            return fromDotEnv.Trim();
        }

        throw new InvalidOperationException(
            $"Missing required environment variable '{variableName}'. " +
            "Set it in the current process environment or in infra/docker/.env.");
    }

    /// <summary>
    /// Attempts to resolve a variable from the repository docker .env file.
    /// </summary>
    /// <param name="variableName">The variable name to resolve.</param>
    /// <returns>The resolved value when present; otherwise <see langword="null"/>.</returns>
    private static string? TryReadValueFromDotEnv(string variableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        if (solutionRoot is null)
        {
            return null;
        }

        var dotEnvPath = Path.Combine(solutionRoot, "infra", "docker", ".env");
        if (!File.Exists(dotEnvPath))
        {
            return null;
        }

        foreach (var rawLine in File.ReadAllLines(dotEnvPath))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.Trim();

            if (line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!string.Equals(key, variableName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }

    /// <summary>
    /// Walks upward from the current directory until the repository root is found.
    /// </summary>
    /// <param name="startDirectory">The starting directory.</param>
    /// <returns>The solution root path when found; otherwise <see langword="null"/>.</returns>
    private static string? FindSolutionRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);

        while (current is not null)
        {
            var srcPath = Path.Combine(current.FullName, "src");
            var infraPath = Path.Combine(current.FullName, "infra");

            if (Directory.Exists(srcPath) && Directory.Exists(infraPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Computes the PayMongo signature header value for the supplied payload.
    /// </summary>
    /// <param name="payload">The exact raw JSON payload to be sent.</param>
    /// <param name="secretKey">The webhook secret key from test configuration.</param>
    /// <returns>The signature header value.</returns>
    private static string ComputePayMongoSignatureHeader(string payload, string secretKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signedPayload = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return $"t={timestamp},te={signature}";
    }

    /// <summary>
    /// Builds a structurally valid checkout_session.paid webhook payload.
    /// </summary>
    /// <param name="eventId">Provider event identifier.</param>
    /// <param name="checkoutSessionId">Provider checkout session identifier.</param>
    /// <param name="paymentAttemptId">ExitPass payment attempt identifier.</param>
    /// <param name="parkingSessionId">ExitPass parking session identifier.</param>
    /// <param name="requestedByUserId">The internal user identifier required by POA.</param>
    /// <returns>A serialized webhook payload.</returns>
    private static string BuildValidCheckoutSessionPaidPayload(
        string eventId,
        string checkoutSessionId,
        string paymentAttemptId,
        string parkingSessionId,
        string requestedByUserId)
    {
        var body = new
        {
            data = new
            {
                id = eventId,
                type = "event",
                attributes = new
                {
                    type = "checkout_session.paid",
                    livemode = false,
                    data = new
                    {
                        id = checkoutSessionId,
                        type = "checkout_session",
                        attributes = new
                        {
                            payments = Array.Empty<object>(),
                            metadata = new Dictionary<string, string>
                            {
                                ["payment_attempt_id"] = paymentAttemptId,
                                ["parking_session_id"] = parkingSessionId,
                                ["requested_by_user_id"] = requestedByUserId
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// Builds a structurally valid webhook payload that preserves payment_attempt_id
    /// but omits parking_session_id so the handler can reject it deterministically.
    /// </summary>
    /// <param name="eventId">Provider event identifier.</param>
    /// <param name="checkoutSessionId">Provider checkout session identifier.</param>
    /// <param name="paymentAttemptId">ExitPass payment attempt identifier.</param>
    /// <param name="requestedByUserId">The internal user identifier required by POA.</param>
    /// <returns>A serialized webhook payload.</returns>
    private static string BuildPayloadMissingParkingSessionId(
        string eventId,
        string checkoutSessionId,
        string paymentAttemptId,
        string requestedByUserId)
    {
        var body = new
        {
            data = new
            {
                id = eventId,
                type = "event",
                attributes = new
                {
                    type = "checkout_session.paid",
                    livemode = false,
                    data = new
                    {
                        id = checkoutSessionId,
                        type = "checkout_session",
                        attributes = new
                        {
                            payments = Array.Empty<object>(),
                            metadata = new Dictionary<string, string>
                            {
                                ["payment_attempt_id"] = paymentAttemptId,
                                ["requested_by_user_id"] = requestedByUserId
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// Builds a structurally valid webhook payload that preserves payment_attempt_id
    /// but omits requested_by_user_id so the handler can reject it deterministically.
    /// </summary>
    /// <param name="eventId">Provider event identifier.</param>
    /// <param name="checkoutSessionId">Provider checkout session identifier.</param>
    /// <param name="paymentAttemptId">ExitPass payment attempt identifier.</param>
    /// <param name="parkingSessionId">ExitPass parking session identifier.</param>
    /// <returns>A serialized webhook payload.</returns>
    private static string BuildPayloadMissingRequestedByUserId(
        string eventId,
        string checkoutSessionId,
        string paymentAttemptId,
        string parkingSessionId)
    {
        var body = new
        {
            data = new
            {
                id = eventId,
                type = "event",
                attributes = new
                {
                    type = "checkout_session.paid",
                    livemode = false,
                    data = new
                    {
                        id = checkoutSessionId,
                        type = "checkout_session",
                        attributes = new
                        {
                            payments = Array.Empty<object>(),
                            metadata = new Dictionary<string, string>
                            {
                                ["payment_attempt_id"] = paymentAttemptId,
                                ["parking_session_id"] = parkingSessionId
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// Builds a structurally valid but non-authoritative payment.paid payload for the
    /// PayMongo checkout-session rail.
    /// </summary>
    /// <param name="eventId">Provider event identifier.</param>
    /// <param name="paymentId">Provider payment identifier.</param>
    /// <param name="paymentAttemptId">ExitPass payment attempt identifier.</param>
    /// <returns>A serialized webhook payload.</returns>
    private static string BuildNonAuthoritativePaymentPaidPayload(
        string eventId,
        string paymentId,
        string paymentAttemptId)
    {
        var body = new
        {
            data = new
            {
                id = eventId,
                type = "event",
                attributes = new
                {
                    type = "payment.paid",
                    livemode = false,
                    data = new
                    {
                        id = paymentId,
                        type = "payment",
                        attributes = new
                        {
                            amount = 5000,
                            currency = "PHP",
                            metadata = new Dictionary<string, string>
                            {
                                ["payment_attempt_id"] = paymentAttemptId
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }
}
