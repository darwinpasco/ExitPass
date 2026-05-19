using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.ProviderWebhooks;

/// <summary>
/// Integration tests for the Payment Orchestrator to Central PMS verified-outcome boundary.
///
/// BRD implemented:
/// - Section 9.10, Payment Processing and Confirmation
/// - Section 9.13, Timeout, Retry, and Duplicate Handling
/// - Section 12, Payment Orchestration
///
/// SDD implemented:
/// - Section 10.5.2, Payment Provider Webhook
/// - Section 10.5.3, Report Verified Payment Outcome
/// - Section 10.7, Idempotency and Concurrency Rules
///
/// System invariants enforced:
/// - Only authentic provider callbacks may cross the POA-to-Central PMS boundary.
/// - POA reports provider-neutral verified outcome evidence only.
/// - Central PMS remains the sole authority for PaymentAttempt finality, PaymentConfirmation, and ExitAuthorization.
/// </summary>
public sealed class PaymentOrchestratorCentralPmsBoundaryIntegrationTests
    : IClassFixture<PaymentOrchestratorWebApplicationFactory>
{
    private const string WebhookRoute = "/v1/provider/paymongo/webhooks";

    private readonly PaymentOrchestratorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentOrchestratorCentralPmsBoundaryIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">The shared POA web application factory.</param>
    public PaymentOrchestratorCentralPmsBoundaryIntegrationTests(PaymentOrchestratorWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _factory.ResetBoundaryState();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Verifies that an authentic provider success webhook is reported to Central PMS
    /// as provider-neutral evidence and not as POA-owned finality.
    /// </summary>
    [Fact]
    public async Task Reports_verified_success_outcome_to_central_pms_without_claiming_finality()
    {
        var paymentAttemptId = Guid.Parse("81000000-0000-0000-0000-000000000001");
        var parkingSessionId = Guid.Parse("82000000-0000-0000-0000-000000000001");
        var requestedByUserId = Guid.Parse("83000000-0000-0000-0000-000000000001");
        var correlationId = Guid.Parse("84000000-0000-0000-0000-000000000001");
        var providerSessionId = "cs_boundary_success_001";
        var eventId = "evt_boundary_success_001";

        var payload = BuildWebhookPayload(
            eventId,
            "checkout_session.payment.paid",
            providerSessionId,
            paymentAttemptId,
            parkingSessionId,
            requestedByUserId,
            correlationId);

        using var request = CreateSignedWebhookRequest(payload);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Contains("finalAttemptStatus", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        body.Contains("exitAuthorization", StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        var report = _factory.CapturedCentralPmsReports.Should().ContainSingle().Subject;
        report.PaymentAttemptId.Should().Be(paymentAttemptId);
        report.ParkingSessionId.Should().Be(parkingSessionId);
        report.RequestedByUserId.Should().Be(requestedByUserId);
        report.CorrelationId.Should().Be(correlationId);
        report.ProviderCode.Should().Be("PAYMONGO");
        report.ProviderReference.Should().Be(providerSessionId);
        report.ProviderSessionId.Should().Be(providerSessionId);
        report.CanonicalStatus.Should().Be("SUCCEEDED");
        report.EventId.Should().Be(eventId);
        report.IsTerminal.Should().BeTrue();
        report.IsSuccess.Should().BeTrue();
        report.AmountMinor.Should().Be(12_500);
        report.Currency.Should().Be("PHP");
        report.RawAttributes.Should().ContainKey("provider_event_id").WhoseValue.Should().Be(eventId);

        AssertReportDoesNotExposeExitAuthorization(report);
    }

    /// <summary>
    /// Verifies that an invalid provider signature is rejected before any verified
    /// outcome is reported to Central PMS.
    /// </summary>
    [Fact]
    public async Task Rejects_invalid_signature_without_reporting_to_central_pms()
    {
        var payload = BuildWebhookPayload(
            "evt_boundary_invalid_signature_001",
            "checkout_session.payment.paid",
            "cs_boundary_invalid_signature_001",
            Guid.Parse("81000000-0000-0000-0000-000000000002"),
            Guid.Parse("82000000-0000-0000-0000-000000000002"),
            Guid.Parse("83000000-0000-0000-0000-000000000002"),
            Guid.Parse("84000000-0000-0000-0000-000000000002"));

        using var request = CreateSignedWebhookRequest(payload, "not-the-real-secret");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.CapturedCentralPmsReports.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that provider failure, cancellation, and expiry are reported as
    /// provider-neutral non-success outcomes and do not create false successful finality.
    /// </summary>
    /// <param name="eventType">The PayMongo provider event type.</param>
    /// <param name="expectedCanonicalStatus">The expected provider-neutral canonical status.</param>
    [Theory]
    [InlineData("payment.failed", "FAILED")]
    [InlineData("payment.cancelled", "CANCELLED")]
    [InlineData("payment.expired", "EXPIRED")]
    public async Task Reports_terminal_non_success_outcomes_without_claiming_success_finality(
        string eventType,
        string expectedCanonicalStatus)
    {
        _factory.ResetBoundaryState();

        var suffix = expectedCanonicalStatus.ToLowerInvariant();
        var payload = BuildWebhookPayload(
            $"evt_boundary_{suffix}_001",
            eventType,
            $"pay_boundary_{suffix}_001",
            Guid.Parse($"81000000-0000-0000-0000-0000000001{expectedCanonicalStatus.Length:D2}"),
            Guid.Parse($"82000000-0000-0000-0000-0000000001{expectedCanonicalStatus.Length:D2}"),
            Guid.Parse($"83000000-0000-0000-0000-0000000001{expectedCanonicalStatus.Length:D2}"),
            Guid.Parse($"84000000-0000-0000-0000-0000000001{expectedCanonicalStatus.Length:D2}"),
            checkoutSessionId: $"cs_boundary_{suffix}_001");

        using var request = CreateSignedWebhookRequest(payload);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = _factory.CapturedCentralPmsReports.Should().ContainSingle().Subject;
        report.CanonicalStatus.Should().Be(expectedCanonicalStatus);
        report.IsTerminal.Should().BeTrue();
        report.IsSuccess.Should().BeFalse();
        report.ProviderCode.Should().Be("PAYMONGO");
        report.RawAttributes.Should().ContainKey("event_type").WhoseValue.Should().Be(eventType);

        AssertReportDoesNotExposeExitAuthorization(report);
    }

    /// <summary>
    /// Verifies that duplicate provider events are handled idempotently and do not
    /// create duplicate Central PMS reports.
    /// </summary>
    [Fact]
    public async Task Reports_duplicate_provider_event_only_once()
    {
        var payload = BuildWebhookPayload(
            "evt_boundary_duplicate_001",
            "checkout_session.payment.paid",
            "cs_boundary_duplicate_001",
            Guid.Parse("81000000-0000-0000-0000-000000000003"),
            Guid.Parse("82000000-0000-0000-0000-000000000003"),
            Guid.Parse("83000000-0000-0000-0000-000000000003"),
            Guid.Parse("84000000-0000-0000-0000-000000000003"));

        using var firstRequest = CreateSignedWebhookRequest(payload);
        var firstResponse = await _client.SendAsync(firstRequest);

        using var secondRequest = CreateSignedWebhookRequest(payload);
        var secondResponse = await _client.SendAsync(secondRequest);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.CapturedCentralPmsReports.Should().ContainSingle();
    }

    private static void AssertReportDoesNotExposeExitAuthorization(VerifiedPaymentOutcomeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        typeof(VerifiedPaymentOutcomeReport)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(propertyName => propertyName.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
    }

    private HttpRequestMessage CreateSignedWebhookRequest(string payload, string? overrideSecretKey = null)
    {
        var secretKey = string.IsNullOrWhiteSpace(overrideSecretKey)
            ? _factory.PayMongoWebhookSecretKey
            : overrideSecretKey;

        var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation(
            "paymongo-signature",
            ComputePayMongoSignatureHeader(payload, secretKey));

        return request;
    }

    private static string ComputePayMongoSignatureHeader(string payload, string secretKey)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signedPayload = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        return $"t={timestamp},te={signature}";
    }

    private static string BuildWebhookPayload(
        string eventId,
        string eventType,
        string providerReference,
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid requestedByUserId,
        Guid correlationId,
        string? checkoutSessionId = null)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["amount"] = 12_500,
            ["currency"] = "PHP",
            ["metadata"] = new Dictionary<string, string>
            {
                ["payment_attempt_id"] = paymentAttemptId.ToString(),
                ["parking_session_id"] = parkingSessionId.ToString(),
                ["requested_by_user_id"] = requestedByUserId.ToString(),
                ["correlation_id"] = correlationId.ToString()
            }
        };

        if (!string.IsNullOrWhiteSpace(checkoutSessionId))
        {
            attributes["checkout_session_id"] = checkoutSessionId;
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
                    livemode = false,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    data = new
                    {
                        id = providerReference,
                        type = eventType.StartsWith("checkout_session.", StringComparison.OrdinalIgnoreCase)
                            ? "checkout_session"
                            : "payment",
                        attributes
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body);
    }
}
