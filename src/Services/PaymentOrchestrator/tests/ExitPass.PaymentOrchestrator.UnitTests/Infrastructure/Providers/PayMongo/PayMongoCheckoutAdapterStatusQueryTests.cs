using System.Net;
using System.Net.Http.Headers;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Providers.PayMongo;

/// <summary>
/// Unit tests for PayMongo checkout-session status query mapping.
/// </summary>
public sealed class PayMongoCheckoutAdapterStatusQueryTests
{
    private static readonly Guid CorrelationId = Guid.Parse("70638d38-2f84-4c07-b708-38deebddbb34");

    [Fact]
    public async Task RetrieveCheckoutSessionStatusAsync_UsesGetWithProviderSessionReferenceAndBasicAuth()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, StatusResponse()));
        var client = CreateClient(handler);

        var result = await client.RetrieveCheckoutSessionStatusAsync("cs_status_001", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.paymongo.test/v1/checkout_sessions/cs_status_001", handler.Request.RequestUri!.ToString());
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
        Assert.NotEmpty(handler.Request.Headers.Authorization.Parameter!);
        Assert.Equal("cs_status_001", result.CheckoutSessionId);
        Assert.Equal("pay_status_001", result.ProviderReference);
        Assert.Equal("paid", result.SourceStatus);
        Assert.Equal(10000, result.AmountMinor);
        Assert.Equal("PHP", result.CurrencyCode);
    }

    [Theory]
    [InlineData("paid", CanonicalPaymentOutcomeStatus.Succeeded, true, true, false, true)]
    [InlineData("succeeded", CanonicalPaymentOutcomeStatus.Succeeded, true, true, false, true)]
    [InlineData("failed", CanonicalPaymentOutcomeStatus.Failed, true, false, false, false)]
    [InlineData("expired", CanonicalPaymentOutcomeStatus.Expired, true, false, false, false)]
    [InlineData("cancelled", CanonicalPaymentOutcomeStatus.Cancelled, true, false, false, false)]
    [InlineData("canceled", CanonicalPaymentOutcomeStatus.Cancelled, true, false, false, false)]
    [InlineData("pending", CanonicalPaymentOutcomeStatus.PendingProvider, false, false, true, false)]
    [InlineData("processing", CanonicalPaymentOutcomeStatus.PendingProvider, false, false, true, false)]
    [InlineData("awaiting_payment", CanonicalPaymentOutcomeStatus.PendingProvider, false, false, true, false)]
    public async Task QueryProviderSessionStatusAsync_MapsKnownStatusesToProviderNeutralEvidence(
        string sourceStatus,
        CanonicalPaymentOutcomeStatus expectedStatus,
        bool expectedTerminal,
        bool expectedSuccess,
        bool expectedRetryable,
        bool expectedReportable)
    {
        var adapter = CreateAdapter(StatusResponse(status: sourceStatus));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.Equal("PAYMONGO", result.ProviderCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", result.ProviderProduct);
        Assert.Equal("cs_status_001", result.ProviderSessionId);
        Assert.Equal("pay_status_001", result.ProviderReference);
        Assert.Equal(sourceStatus, result.SourceStatus);
        Assert.Equal(expectedStatus, result.NormalizedStatus);
        Assert.Equal(expectedTerminal, result.IsTerminal);
        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(expectedRetryable, result.Retryable);
        Assert.Equal(expectedReportable, result.ReportableToCentralPms);
        Assert.Equal(10000, result.AmountMinor);
        Assert.Equal("PHP", result.CurrencyCode);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenStatusIsUnknown_FailsClosedWithoutReportableFinality()
    {
        var adapter = CreateAdapter(StatusResponse(status: "surprising_provider_state"));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.Equal(CanonicalPaymentOutcomeStatus.PendingProvider, result.NormalizedStatus);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
        Assert.False(result.Retryable);
        Assert.False(result.ReportableToCentralPms);
        Assert.Equal("PAYMONGO_STATUS_QUERY_UNKNOWN_STATUS", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenPayloadIsMalformed_ReturnsNonReportableFailure()
    {
        var adapter = CreateAdapter("{ not valid json");

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsTerminal);
        Assert.Equal("PAYMONGO_STATUS_QUERY_MALFORMED_RESPONSE", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenRequiredFieldsAreMissing_ReturnsNonReportableFailure()
    {
        var adapter = CreateAdapter("""
            {
              "data": {
                "attributes": {
                  "status": "paid"
                }
              }
            }
            """);

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.IsSuccess);
        Assert.Equal("PAYMONGO_STATUS_QUERY_MALFORMED_RESPONSE", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenAmountDiffers_ReturnsMismatchWithoutReportableFinality()
    {
        var adapter = CreateAdapter(StatusResponse(amountMinor: 9999));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsTerminal);
        Assert.Equal("PAYMONGO_STATUS_QUERY_AMOUNT_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenCurrencyDiffers_ReturnsMismatchWithoutReportableFinality()
    {
        var adapter = CreateAdapter(StatusResponse(currencyCode: "USD"));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsTerminal);
        Assert.Equal("PAYMONGO_STATUS_QUERY_CURRENCY_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenProviderReferenceDiffers_ReturnsMismatchWithoutReportableFinality()
    {
        var adapter = CreateAdapter(StatusResponse(providerReference: "pay_different_001"));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsTerminal);
        Assert.Equal("PAYMONGO_STATUS_QUERY_PROVIDER_REFERENCE_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenHttp404_ReturnsDeterministicNotFoundFailure()
    {
        var adapter = CreateAdapter(HttpStatusCode.NotFound, ProviderErrorResponse("resource_not_found"));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.Retryable);
        Assert.Equal("PAYMONGO_STATUS_QUERY_PROVIDER_SESSION_NOT_FOUND", result.ErrorCode);
        Assert.Equal("404", result.Diagnostics["http_status_code"]);
        Assert.Equal("resource_not_found", result.Diagnostics["provider_reason_code"]);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenHttp5xx_ReturnsRetryableProviderUnavailable()
    {
        var adapter = CreateAdapter(HttpStatusCode.BadGateway, ProviderErrorResponse("upstream_error"));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.True(result.Retryable);
        Assert.Equal("PAYMONGO_STATUS_QUERY_PROVIDER_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenHttpTimeout_ReturnsRetryableTimeout()
    {
        var adapter = CreateAdapter(new StubHttpMessageHandler(_ => throw new TaskCanceledException("timeout")));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.False(result.ReportableToCentralPms);
        Assert.True(result.Retryable);
        Assert.Equal("PAYMONGO_STATUS_QUERY_TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_DoesNotExposeSecretsInFailureDiagnostics()
    {
        var adapter = CreateAdapter(
            HttpStatusCode.BadRequest,
            """
            {
              "errors": [
                {
                  "code": "parameter_invalid",
                  "detail": "sk_test_secret and whsec_secret must not leak"
                }
              ]
            }
            """);

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);
        var diagnosticText = string.Join(" ", result.Diagnostics.Values);

        Assert.Equal("PAYMONGO_STATUS_QUERY_PROVIDER_REJECTED", result.ErrorCode);
        Assert.DoesNotContain("sk_test_secret", result.ErrorMessage);
        Assert.DoesNotContain("whsec_secret", result.ErrorMessage);
        Assert.DoesNotContain("sk_test_secret", diagnosticText);
        Assert.DoesNotContain("whsec_secret", diagnosticText);
    }

    [Fact]
    public async Task QueryProviderSessionStatusAsync_WhenTerminalSuccess_ReturnsReportableEvidenceButDoesNotCallCentralPms()
    {
        var adapter = CreateAdapter(StatusResponse(status: "paid"));

        var result = await adapter.QueryProviderSessionStatusAsync(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsTerminal);
        Assert.True(result.IsSuccess);
        Assert.True(result.ReportableToCentralPms);
        Assert.Null(result.ErrorCode);
    }

    private static PayMongoCheckoutAdapter CreateAdapter(string responseJson)
    {
        return CreateAdapter(HttpStatusCode.OK, responseJson);
    }

    private static PayMongoCheckoutAdapter CreateAdapter(HttpStatusCode statusCode, string responseJson)
    {
        return CreateAdapter(new StubHttpMessageHandler(_ => CreateJsonResponse(statusCode, responseJson)));
    }

    private static PayMongoCheckoutAdapter CreateAdapter(StubHttpMessageHandler handler)
    {
        var options = CreateOptions();

        return new PayMongoCheckoutAdapter(
            new PayMongoClient(new HttpClient(handler), options),
            options);
    }

    private static PayMongoClient CreateClient(StubHttpMessageHandler handler)
    {
        return new PayMongoClient(new HttpClient(handler), CreateOptions());
    }

    private static IOptions<PayMongoOptions> CreateOptions()
    {
        return Options.Create(new PayMongoOptions
        {
            BaseUrl = "https://api.paymongo.test",
            SecretKey = "sk_test_unit",
            PublicKey = "pk_test_unit",
            WebhookSecretKey = "whsec_unit",
            IsLiveMode = false,
            AllowedPaymentMethodTypes = new[] { "qrph" }
        });
    }

    private static ProviderStatusQueryCommand DefaultCommand()
    {
        return new ProviderStatusQueryCommand(
            "cs_status_001",
            "pay_status_001",
            10000,
            "PHP",
            CorrelationId);
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
    }

    private static string ProviderErrorResponse(string code)
    {
        return $$"""
            {
              "errors": [
                {
                  "code": "{{code}}",
                  "detail": "safe provider error detail"
                }
              ]
            }
            """;
    }

    private static string StatusResponse(
        string status = "paid",
        string checkoutSessionId = "cs_status_001",
        string providerReference = "pay_status_001",
        long amountMinor = 10000,
        string currencyCode = "PHP")
    {
        return $$"""
            {
              "data": {
                "id": "{{checkoutSessionId}}",
                "type": "checkout_session",
                "attributes": {
                  "status": "{{status}}",
                  "updated_at": 1775470400,
                  "payments": [
                    {
                      "id": "{{providerReference}}",
                      "type": "payment",
                      "attributes": {
                        "amount": {{amountMinor}},
                        "currency": "{{currencyCode}}",
                        "status": "{{status}}",
                        "updated_at": 1775470400
                      }
                    }
                  ]
                }
              }
            }
            """;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            if (request.Headers.Authorization is not AuthenticationHeaderValue)
            {
                throw new InvalidOperationException("Expected PayMongo authorization header.");
            }

            return Task.FromResult(_responseFactory(request));
        }
    }
}
