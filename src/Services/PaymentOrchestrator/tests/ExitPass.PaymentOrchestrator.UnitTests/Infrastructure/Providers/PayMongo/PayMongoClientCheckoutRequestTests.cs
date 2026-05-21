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
                "/success",
                "/failed",
                "/cancelled",
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
        var metadata = attributes.GetProperty("metadata");

        Assert.Equal("ExitPass Parking Fee - WEBPAY-20260521-FRESH-001", lineItemName);
        Assert.Equal(
            "Site: WebPay Test Site 2026-05-21  Ticket: WEBPAY-20260521-FRESH-001  Plate: WEBPAY001",
            description);
        Assert.DoesNotContain("Amount:", description);
        Assert.DoesNotContain("PHP 100.00", description);
        Assert.Equal("WEBPAY-20260521-FRESH-001", referenceNumber);
        Assert.DoesNotMatch(GuidRegex, lineItemName!);
        Assert.DoesNotMatch(GuidRegex, description!);
        Assert.DoesNotMatch(GuidRegex, referenceNumber!);
        Assert.Equal(paymentAttemptId.ToString(), metadata.GetProperty("payment_attempt_id").GetString());
        Assert.Equal(parkingSessionId.ToString(), metadata.GetProperty("parking_session_id").GetString());
        Assert.Equal(tariffSnapshotId.ToString(), metadata.GetProperty("tariff_snapshot_id").GetString());
        Assert.Equal(correlationId.ToString(), metadata.GetProperty("correlation_id").GetString());
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

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "data": {
                        "id": "cs_unit_test",
                        "attributes": {
                          "checkout_url": "https://checkout.paymongo.test/cs_unit_test",
                          "checkout_url_expires_at": "2026-05-21T15:59:59Z"
                        }
                      }
                    }
                    """)
            };
        }
    }
}
