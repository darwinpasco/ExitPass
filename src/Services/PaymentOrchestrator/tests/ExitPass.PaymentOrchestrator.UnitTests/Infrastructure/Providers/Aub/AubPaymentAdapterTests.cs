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
/// Unit tests for the AUB Card Cashier adapter boundary and provider-neutral mapping.
/// </summary>
public sealed class AubPaymentAdapterTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("9c708f54-6daa-4835-a76b-6b166652dd02");

    /// <summary>
    /// Verifies that AUB session creation sends the official Card Cashier H5 request shape.
    /// </summary>
    [Fact]
    public async Task AubClient_WhenCreatePaymentRequested_SendsOfficialRequestShape()
    {
        var handler = new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse());
        var signer = new FakeAubRequestSigner();
        var adapter = CreateAdapter(handler, signer);

        var result = await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal(ProviderCode.Aub, adapter.ProviderCode);
        Assert.Equal(ProviderProductCode.AubCardCashier, adapter.ProviderProduct);
        Assert.Equal(PaymentAttemptId.ToString(), result.ProviderSessionId);
        Assert.Equal(PaymentAttemptId.ToString(), result.ProviderReference);
        Assert.Equal("PENDING_PROVIDER", result.SessionStatus);
        Assert.Equal(ProviderHandoffType.Redirect, result.Handoff.Type);
        Assert.Equal("https://aub.test/gateway/payment/cashier/v1/payment", handler.LastRequestUri.ToString());

        using var document = JsonDocument.Parse(handler.LastRequestBody);
        var orderInformation = document.RootElement.GetProperty("orderInformation");

        Assert.Equal(12500, orderInformation.GetProperty("amount").GetInt64());
        Assert.Equal(PaymentAttemptId.ToString(), orderInformation.GetProperty("orderId").GetString());
        Assert.Equal("ExitPass parking payment", orderInformation.GetProperty("goodsDetail").GetString());
        Assert.Equal("idem-aub-unit-001", orderInformation.GetProperty("attach").GetString());
        Assert.Equal("https://exitpass.test/payments/success", orderInformation.GetProperty("callbackUrl").GetString());
        Assert.Equal("https://exitpass.test/provider/aub/webhook", orderInformation.GetProperty("notifyUrl").GetString());
        Assert.Equal(10, orderInformation.GetProperty("validityPeriod").GetInt32());
    }

    /// <summary>
    /// Verifies that official AUB authorization and request identity headers are sent through the signer boundary.
    /// </summary>
    [Fact]
    public async Task AubClient_WhenAuthHeadersRequired_SendsOfficialAuthHeaders()
    {
        var handler = new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse());
        var signer = new FakeAubRequestSigner();
        var adapter = CreateAdapter(handler, signer);

        await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal("idem-aub-unit-001", handler.LastCustomerRequestId);
        Assert.Equal("application/json", handler.LastAccept);
        Assert.Equal("en-US", handler.LastAcceptLanguage);
        Assert.Equal("AUB-TEST-SIGNATURE", handler.LastAuthorization);
        Assert.NotNull(handler.LastDate);
        Assert.Equal("POST", signer.LastSignedRequest.Method);
        Assert.Equal("/gateway/payment/cashier/v1/payment", signer.LastSignedRequest.RequestPath);
        Assert.Equal("idem-aub-unit-001", signer.LastSignedRequest.CustomerRequestId);
    }

    /// <summary>
    /// Verifies that an accepted AUB H5 response remains a pending provider outcome until a verified notification arrives.
    /// </summary>
    [Fact]
    public async Task AubClient_WhenProviderReturnsPending_MapsToPendingProvider()
    {
        var adapter = CreateAdapter(new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse()));

        var result = await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal("PENDING_PROVIDER", result.SessionStatus);
        Assert.Equal("https://aub.test/cashier/order-001", result.Handoff.RedirectUrl);
    }

    /// <summary>
    /// Verifies that an official success notification maps to a successful terminal outcome.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenOfficialSuccessPayloadReceived_MapsToSucceeded()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(CreateWebhookRequest("SUCCESS"), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Succeeded, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentAttemptId, result.PaymentAttemptId);
        Assert.Equal("aub-ref-001", result.ProviderReference);
        Assert.Equal(PaymentAttemptId.ToString(), result.ProviderSessionId);
        Assert.Equal("58931a43-eef2-43fa-887a-38a9874d72e7", result.RawAttributes["parking_session_id"]);
        Assert.Equal("VISA", result.RawAttributes["card_payment_brand"]);
    }

    /// <summary>
    /// Verifies that an official failed notification maps to failed evidence and never a success outcome.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenOfficialFailedPayloadReceived_MapsToFailed()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(CreateWebhookRequest("FAILED"), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.Failed, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that defensive cancelled and expired AUB notification states normalize deterministically.
    /// </summary>
    [Theory]
    [InlineData("CANCELLED", CanonicalPaymentOutcomeStatus.Cancelled)]
    [InlineData("EXPIRED", CanonicalPaymentOutcomeStatus.Expired)]
    public async Task AubWebhook_WhenOfficialTerminalPayloadReceived_MapsToTerminalNonSuccess(
        string providerStatus,
        CanonicalPaymentOutcomeStatus expectedStatus)
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(CreateWebhookRequest(providerStatus), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(expectedStatus, result.CanonicalStatus);
        Assert.True(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that pending AUB transaction results remain non-terminal provider evidence.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenOfficialPendingPayloadReceived_MapsToPendingProvider()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(CreateWebhookRequest("PENDING"), CancellationToken.None);

        Assert.True(result.IsAuthentic);
        Assert.Equal(CanonicalPaymentOutcomeStatus.PendingProvider, result.CanonicalStatus);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Verifies that malformed official AUB notification payloads fail closed before provider evidence is accepted.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenMalformedPayloadReceived_FailsClosed()
    {
        var adapter = CreateAdapter();

        var result = await adapter.VerifyWebhookAsync(
            new ProviderWebhookRequest(
                Headers: new Dictionary<string, string>(),
                RawBody: "{\"code\":\"00\",\"message\":\"Success\"}"),
            CancellationToken.None);

        Assert.False(result.IsAuthentic);
        Assert.Equal("AUB_WEBHOOK_MISSING_DATA", result.EventId);
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
    /// Verifies that unsuccessful AUB response codes do not generate successful provider sessions.
    /// </summary>
    [Fact]
    public async Task AubProvider_WhenProviderResponseMalformed_ReturnsProviderError()
    {
        var response = JsonSerializer.Serialize(new
        {
            code = "09",
            message = "PARAMETER IS EMPTY OR MALFORMED"
        });
        var adapter = CreateAdapter(new CapturingAubHandler(HttpStatusCode.OK, response));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None));

        Assert.Contains("09", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that AUB-specific DTO fields do not leak into provider-neutral Payment Orchestrator contracts.
    /// </summary>
    [Fact]
    public void AubProvider_DoesNotLeakAubSpecificFieldsIntoProviderNeutralContracts()
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
    /// Verifies that AUB tests use fake local provider values and do not require live credentials or URLs.
    /// </summary>
    [Fact]
    public async Task AubProvider_DoesNotUseLiveCredentialsOrUrlsInTests()
    {
        var handler = new CapturingAubHandler(HttpStatusCode.OK, PaymentSessionResponse());
        var adapter = CreateAdapter(handler);

        await adapter.CreatePaymentSessionAsync(CreateCommand(), CancellationToken.None);

        Assert.Equal("aub.test", handler.LastRequestUri.Host);
        Assert.DoesNotContain("paymentapi.wepayez.com", handler.LastRequestUri.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.aub", handler.LastRequestUri.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prod", handler.LastRequestUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static AubPaymentAdapter CreateAdapter(HttpMessageHandler? handler = null, IAubRequestSigner? requestSigner = null)
    {
        var options = Options.Create(new AubOptions
        {
            BaseUrl = "https://aub.test/gateway/payment",
            MerchantId = "merchant-unit-test"
        });

        var client = new AubClient(
            new HttpClient(handler ?? new ThrowingHttpMessageHandler()),
            options,
            requestSigner ?? new FakeAubRequestSigner());
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

    private static ProviderWebhookRequest CreateWebhookRequest(string status)
    {
        var body = new
        {
            code = "00",
            message = "Success",
            data = new
            {
                orderInformation = new
                {
                    referencedId = "aub-ref-001",
                    orderId = PaymentAttemptId.ToString(),
                    goodsDetail = "ExitPass parking payment",
                    attach = "parking_session_id=58931a43-eef2-43fa-887a-38a9874d72e7;requested_by_user_id=unit-test",
                    currency = "PHP",
                    amount = 12500,
                    responseDate = "2026-05-16T08:00:00Z",
                    transactionResult = status,
                    paymentType = "PAY",
                    paymentBrand = "VISA"
                },
                card = new
                {
                    paymentBrand = "VISA",
                    cardBin = "420000",
                    last4Digits = "0000"
                }
            }
        };

        return new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>(),
            RawBody: JsonSerializer.Serialize(body));
    }

    private static string PaymentSessionResponse()
    {
        var body = new
        {
            code = "00",
            message = "Approved",
            data = new
            {
                cashierUrl = "https://aub.test/cashier/order-001",
                orderInformation = new
                {
                    orderId = PaymentAttemptId.ToString(),
                    goodsDetail = "ExitPass parking payment",
                    attach = "idem-aub-unit-001",
                    currency = "PHP",
                    amount = 12500,
                    paymentType = "PAY",
                    responseDate = "2026-05-16T08:00:00Z"
                }
            }
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

        public string? LastCustomerRequestId { get; private set; }

        public string? LastAccept { get; private set; }

        public string? LastAcceptLanguage { get; private set; }

        public string? LastAuthorization { get; private set; }

        public DateTimeOffset? LastDate { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri ?? new Uri("https://aub.test/missing");
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastCustomerRequestId = request.Headers.TryGetValues("Customer-Request-Id", out var customerRequestIdValues)
                ? customerRequestIdValues.Single()
                : null;
            LastAccept = request.Headers.Accept.SingleOrDefault()?.MediaType;
            LastAcceptLanguage = request.Headers.AcceptLanguage.SingleOrDefault()?.Value;
            LastAuthorization = request.Headers.TryGetValues("Authorization", out var authorizationValues)
                ? authorizationValues.Single()
                : null;
            LastDate = request.Headers.Date;

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeAubRequestSigner : IAubRequestSigner
    {
        public AubSignedRequest LastSignedRequest { get; private set; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            DateTimeOffset.UnixEpoch);

        public string CreateAuthorizationHeader(AubSignedRequest request)
        {
            LastSignedRequest = request;
            return "AUB-TEST-SIGNATURE";
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
