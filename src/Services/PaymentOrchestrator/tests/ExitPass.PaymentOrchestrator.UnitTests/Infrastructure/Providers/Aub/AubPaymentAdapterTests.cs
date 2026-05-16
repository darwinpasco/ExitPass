using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Contracts.Providers;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Providers.Aub;

/// <summary>
/// Unit tests for the AUB provider adapter boundary and provider-neutral mapping.
/// </summary>
public sealed class AubPaymentAdapterTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("9c708f54-6daa-4835-a76b-6b166652dd02");

    /// <summary>
    /// Verifies that AUB session creation sends the provider-specific request shape behind the adapter boundary.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenCreatePaymentRequested_SendsProviderSpecificRequestShape()
    {
        var handler = new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse("ACCEPTED"));
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(ProviderCode.Aub, adapter.ProviderCode);
        Assert.Equal(ProviderProductCode.AubCardCashier, adapter.ProviderProduct);
        Assert.Equal("aub_session_001", result.ProviderSessionId);
        Assert.Equal("aub_ref_001", result.ProviderReference);
        Assert.Equal("PENDING_PROVIDER", result.SessionStatus);
        Assert.Equal(ProviderHandoffType.Redirect, result.Handoff.Type);
        Assert.Equal(new Uri("https://aub.test/v1/payments"), handler.LastRequestUri);
        Assert.Equal("idem-aub-unit-001", handler.LastIdempotencyKey);

        using var document = JsonDocument.Parse(handler.LastRequestBody);
        var root = document.RootElement;

        Assert.Equal("merchant-unit-test", root.GetProperty("merchant_id").GetString());
        Assert.Equal(PaymentAttemptId.ToString(), root.GetProperty("reference_id").GetString());
        Assert.Equal(12500, root.GetProperty("amount").GetInt64());
        Assert.Equal("PHP", root.GetProperty("currency").GetString());
        Assert.Equal("https://exitpass.test/provider/aub/webhook", root.GetProperty("callback_url").GetString());
        Assert.Equal("unit-test", root.GetProperty("metadata").GetProperty("source").GetString());
    }

    /// <summary>
    /// Verifies that an accepted AUB provider response remains a pending provider outcome.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenProviderAccepts_ReturnsPendingProviderOutcome()
    {
        var adapter = CreateAdapter(new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse("ACCEPTED")));

        var result = await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal("PENDING_PROVIDER", result.SessionStatus);
    }

    /// <summary>
    /// Verifies that AUB paid callbacks normalize to a successful terminal outcome.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenProviderPaid_ReturnsSucceededProviderOutcome()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(
            CreateWebhookRequest("evt_aub_paid_001", "payment.paid", "PAID"),
            CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Succeeded, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentAttemptId, result.PaymentAttemptId);
        Assert.Equal("aub_ref_001", result.ProviderReference);
    }

    /// <summary>
    /// Verifies that AUB failed callbacks normalize to failed evidence and never a success outcome.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenProviderFails_ReturnsFailedProviderOutcome()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(
            CreateWebhookRequest("evt_aub_failed_001", "payment.failed", "FAILED"),
            CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Failed, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that cancelled and expired AUB states normalize deterministically.
    /// </summary>
    [Theory]
    [InlineData("CANCELLED", CanonicalPaymentOutcomeStatus.Cancelled)]
    [InlineData("EXPIRED", CanonicalPaymentOutcomeStatus.Expired)]
    public async Task AubProvider_WhenProviderCancelsOrExpires_ReturnsTerminalNonSuccessOutcome(
        string providerStatus,
        CanonicalPaymentOutcomeStatus expectedStatus)
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(
            CreateWebhookRequest($"evt_aub_{providerStatus.ToLowerInvariant()}_001", "payment.updated", providerStatus),
            CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(expectedStatus, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that malformed AUB callback payloads fail closed before provider evidence is accepted.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenProviderResponseMalformed_ReturnsProviderError()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(
            new ProviderWebhookRequest(
                Headers: new Dictionary<string, string>(),
                RawBody: "{\"event_id\":\"evt_missing_required_fields\"}"),
            CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.Equal("AUB_WEBHOOK_MISSING_EVENT_TYPE", result.EventId);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that unavailable AUB create-session responses stay retryable at the HTTP boundary.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenProviderUnavailable_ReturnsRetryableUnavailable()
    {
        var adapter = CreateAdapter(new CapturingAubHandler(HttpStatusCode.ServiceUnavailable, "{}"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    /// <summary>
    /// Verifies that AUB-specific DTO fields do not leak into provider-neutral Payment Orchestrator contracts.
    /// </summary>
    [Fact]
    public void AubProvider_DoesNotLeakProviderSpecificFieldsIntoProviderNeutralContracts()
    {
        var neutralContractTypes = new[]
        {
            typeof(InitiateProviderPaymentRequest),
            typeof(InitiateProviderPaymentResponse),
            typeof(ProviderHandoffDto),
            typeof(CreateProviderPaymentSessionCommand),
            typeof(CreateProviderPaymentSessionResult),
            typeof(ProviderWebhookVerificationResult)
        };

        foreach (var type in neutralContractTypes)
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property => property.Name.Contains("Aub", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Verifies that AUB tests use fake local provider values and do not require live credentials.
    /// </summary>
    [Fact]
    public async Task AubProvider_DoesNotUseLiveCredentialsInTests()
    {
        var handler = new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse("ACCEPTED"));
        var adapter = CreateAdapter(handler);

        await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal("aub.test", handler.LastRequestUri.Host);
        Assert.DoesNotContain("api.aub", handler.LastRequestUri.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prod", handler.LastRequestUri.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", handler.LastHeaders, StringComparison.OrdinalIgnoreCase);
    }

    private static AubPaymentAdapter CreateAdapter(HttpMessageHandler? handler = null)
    {
        var options = Options.Create(new AubOptions
        {
            BaseUrl = "https://aub.test",
            MerchantId = "merchant-unit-test"
        });

        var client = new AubClient(new HttpClient(handler ?? new ThrowingHttpMessageHandler()), options);
        return new AubPaymentAdapter(client);
    }

    private static CreateProviderPaymentSessionCommand CreateCommand()
    {
        return new CreateProviderPaymentSessionCommand(
            PaymentAttemptId,
            AmountMinor: 12500,
            Currency: "PHP",
            Description: "ExitPass parking payment",
            IdempotencyKey: "idem-aub-unit-001",
            SuccessUrl: "https://exitpass.test/payments/success",
            FailureUrl: "https://exitpass.test/payments/failure",
            CancelUrl: "https://exitpass.test/payments/cancel",
            WebhookUrl: "https://exitpass.test/provider/aub/webhook",
            Metadata: new Dictionary<string, string>
            {
                ["source"] = "unit-test",
                ["payment_attempt_id"] = PaymentAttemptId.ToString()
            });
    }

    private static ProviderWebhookRequest CreateWebhookRequest(
        string eventId,
        string eventType,
        string status)
    {
        var body = new
        {
            event_id = eventId,
            event_type = eventType,
            payment_attempt_id = PaymentAttemptId.ToString(),
            payment_session_id = "aub_session_001",
            reference = "aub_ref_001",
            status,
            occurred_at = "2026-05-16T08:00:00Z",
            amount = 12500,
            currency = "PHP",
            metadata = new Dictionary<string, string>
            {
                ["parking_session_id"] = "58931a43-eef2-43fa-887a-38a9874d72e7"
            }
        };

        return new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>(),
            RawBody: JsonSerializer.Serialize(body));
    }

    private static string PaymentSessionResponse(string status)
    {
        var body = new
        {
            payment_session_id = "aub_session_001",
            reference = "aub_ref_001",
            status,
            redirect_url = "https://aub.test/pay/aub_session_001",
            expires_at = "2026-05-16T08:30:00Z"
        };

        return JsonSerializer.Serialize(body);
    }

    private sealed class CapturingAubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public CapturingAubHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public Uri LastRequestUri { get; private set; } = new("https://aub.test/not-called");

        public string LastRequestBody { get; private set; } = string.Empty;

        public string? LastIdempotencyKey { get; private set; }

        public string LastHeaders { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri ?? new Uri("https://aub.test/missing");
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastIdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : null;
            LastHeaders = string.Join(Environment.NewLine, request.Headers.Select(header => header.Key));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("AUB webhook tests must not call provider HTTP APIs.");
        }
    }
}
