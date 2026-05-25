using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Providers.PayMongo;

/// <summary>
/// Unit tests for PayMongo checkout-session request construction.
/// </summary>
public sealed class PayMongoClientCheckoutRequestTests
{
    private static readonly Regex GuidRegex = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    /// <summary>
    /// Verifies PayMongo customer-facing checkout fields do not expose internal ExitPass UUIDs.
    /// </summary>
    [Fact]
    public async Task CreateCheckoutSessionAsync_UsesParkerFriendlyDisplayFieldsAndKeepsMetadata()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);
        var paymentAttemptId = Guid.Parse("df0b6210-d30e-404d-9d43-d70b2134601e");
        var parkingSessionId = Guid.Parse("9b5ca2a5-391a-464c-9196-08d7d3a0c6c6");
        var tariffSnapshotId = Guid.Parse("fada574c-c6e9-4542-b3a8-0f3828e70695");
        var correlationId = Guid.Parse("342434f4-76ff-496d-aef1-9781b53e5081");

        await client.CreateCheckoutSessionAsync(
            new CreateProviderPaymentSessionCommand(
                paymentAttemptId,
                10000,
                "PHP",
                "Site: WebPay Test Site 2026-05-21  Ticket: WEBPAY-20260521-FRESH-001  Plate: WEBPAY001",
                "webpay-idempotency",
                "https://webpay.test/webpay/payment-return?ticketReference=WEBPAY-20260521-FRESH-001&paymentAttemptId=df0b6210-d30e-404d-9d43-d70b2134601e&correlationId=342434f4-76ff-496d-aef1-9781b53e5081&result=success",
                "/failed",
                "https://webpay.test/webpay/payment-cancelled?ticketReference=WEBPAY-20260521-FRESH-001&paymentAttemptId=df0b6210-d30e-404d-9d43-d70b2134601e&correlationId=342434f4-76ff-496d-aef1-9781b53e5081&result=cancelled",
                "/webhook",
                new Dictionary<string, string>
                {
                    ["payment_attempt_id"] = paymentAttemptId.ToString(),
                    ["parking_session_id"] = parkingSessionId.ToString(),
                    ["tariff_snapshot_id"] = tariffSnapshotId.ToString(),
                    ["correlation_id"] = correlationId.ToString(),
                    ["ticket_reference"] = "WEBPAY-20260521-FRESH-001",
                    ["site_name"] = "WebPay Test Site 2026-05-21",
                    ["plate_number"] = "WEBPAY001"
                },
                "ExitPass Parking Fee - WEBPAY-20260521-FRESH-001"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestJson!);
        var attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
        var description = attributes.GetProperty("description").GetString();
        var lineItemName = attributes.GetProperty("line_items")[0].GetProperty("name").GetString();
        var referenceNumber = attributes.GetProperty("reference_number").GetString();
        var successUrl = attributes.GetProperty("success_url").GetString();
        var cancelUrl = attributes.GetProperty("cancel_url").GetString();
        var metadata = attributes.GetProperty("metadata");

        Assert.Contains("\"success_url\"", handler.RequestJson);
        Assert.Contains("\"cancel_url\"", handler.RequestJson);
        Assert.DoesNotContain("\"successUrl\"", handler.RequestJson);
        Assert.DoesNotContain("\"cancelUrl\"", handler.RequestJson);
        Assert.Contains("/webpay/payment-return", handler.RequestJson);
        Assert.Contains("/webpay/payment-cancelled", handler.RequestJson);
        Assert.Contains("ticketReference=WEBPAY-20260521-FRESH-001", handler.RequestJson);
        Assert.Contains($"paymentAttemptId={paymentAttemptId}", handler.RequestJson);
        Assert.Contains($"correlationId={correlationId}", handler.RequestJson);
        Assert.Contains("result=success", handler.RequestJson);
        Assert.Contains("result=cancelled", handler.RequestJson);
        Assert.Equal("ExitPass Parking Fee - WEBPAY-20260521-FRESH-001", lineItemName);
        Assert.Equal(
            "Site: WebPay Test Site 2026-05-21  Ticket: WEBPAY-20260521-FRESH-001  Plate: WEBPAY001",
            description);
        Assert.DoesNotContain("Amount:", description);
        Assert.DoesNotContain("PHP 100.00", description);
        Assert.Equal("WEBPAY-20260521-FRESH-001", referenceNumber);
        Assert.Equal(
            "https://webpay.test/webpay/payment-return?ticketReference=WEBPAY-20260521-FRESH-001&paymentAttemptId=df0b6210-d30e-404d-9d43-d70b2134601e&correlationId=342434f4-76ff-496d-aef1-9781b53e5081&result=success",
            successUrl);
        Assert.Equal(
            "https://webpay.test/webpay/payment-cancelled?ticketReference=WEBPAY-20260521-FRESH-001&paymentAttemptId=df0b6210-d30e-404d-9d43-d70b2134601e&correlationId=342434f4-76ff-496d-aef1-9781b53e5081&result=cancelled",
            cancelUrl);
        Assert.DoesNotMatch(GuidRegex, lineItemName!);
        Assert.DoesNotMatch(GuidRegex, description!);
        Assert.DoesNotMatch(GuidRegex, referenceNumber!);
        Assert.Equal(paymentAttemptId.ToString(), metadata.GetProperty("payment_attempt_id").GetString());
        Assert.Equal(parkingSessionId.ToString(), metadata.GetProperty("parking_session_id").GetString());
        Assert.Equal(tariffSnapshotId.ToString(), metadata.GetProperty("tariff_snapshot_id").GetString());
        Assert.Equal(correlationId.ToString(), metadata.GetProperty("correlation_id").GetString());
    }

    /// <summary>
    /// Verifies PayMongo option validation catches production-critical omissions without exposing secrets.
    /// </summary>
    [Fact]
    public void PayMongoOptions_WhenRequiredValuesAreMissing_ReturnsSafeValidationErrors()
    {
        var options = new PayMongoOptions
        {
            BaseUrl = "http://api.paymongo.test",
            SecretKey = "",
            PublicKey = "",
            WebhookSecretKey = "",
            AllowedPaymentMethodTypes = new[] { "qrph", "" },
            WebhookSignatureToleranceSeconds = 10
        };

        var errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("SecretKey", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PublicKey", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("WebhookSecretKey", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BaseUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("AllowedPaymentMethodTypes", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("WebhookSignatureToleranceSeconds", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("sk_", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies provider HTTP failures are mapped to sanitized internal exceptions.
    /// </summary>
    [Fact]
    public async Task CreateCheckoutSessionAsync_WhenProviderReturnsError_ThrowsSanitizedProviderException()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.BadRequest, """
            {
              "errors": [
                {
                  "code": "parameter_invalid",
                  "detail": "secret body detail should not leak sk_live_secret"
                }
              ]
            }
            """);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<PayMongoProviderApiException>(() =>
            client.CreateCheckoutSessionAsync(CreateDefaultCommand(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("parameter_invalid", exception.ReasonCode);
        Assert.DoesNotContain("sk_live_secret", exception.Message);
        Assert.DoesNotContain("secret body detail", exception.Message);
    }

    private static PayMongoClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new PayMongoOptions
        {
            BaseUrl = "https://api.paymongo.test",
            SecretKey = "sk_test_unit",
            PublicKey = "pk_test_unit",
            AllowedPaymentMethodTypes = new[] { "qrph" },
            WebhookSecretKey = "whsec_unit",
            IsLiveMode = false
        });

        return new PayMongoClient(httpClient, options);
    }

    private static CreateProviderPaymentSessionCommand CreateDefaultCommand()
    {
        return new CreateProviderPaymentSessionCommand(
            Guid.Parse("df0b6210-d30e-404d-9d43-d70b2134601e"),
            10000,
            "PHP",
            "Site: WebPay Test Site 2026-05-21  Ticket: WEBPAY-20260521-FRESH-001  Plate: WEBPAY001",
            "webpay-idempotency",
            "https://webpay.test/webpay/payment-return?ticketReference=WEBPAY-20260521-FRESH-001",
            "/failed",
            "https://webpay.test/webpay/payment-cancelled?ticketReference=WEBPAY-20260521-FRESH-001",
            "/webhook",
            new Dictionary<string, string>
            {
                ["payment_attempt_id"] = "df0b6210-d30e-404d-9d43-d70b2134601e",
                ["parking_session_id"] = "9b5ca2a5-391a-464c-9196-08d7d3a0c6c6",
                ["ticket_reference"] = "WEBPAY-20260521-FRESH-001"
            },
            "ExitPass Parking Fee - WEBPAY-20260521-FRESH-001");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseJson;

        public CapturingHttpMessageHandler(
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string? responseJson = null)
        {
            _statusCode = statusCode;
            _responseJson = responseJson ?? """
                {
                  "data": {
                    "id": "cs_unit_test",
                    "attributes": {
                      "checkout_url": "https://checkout.paymongo.test/cs_unit_test",
                      "checkout_url_expires_at": "2026-05-21T15:59:59Z"
                    }
                  }
                }
                """;
        }

        public string? RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson)
            };
        }
    }
}
