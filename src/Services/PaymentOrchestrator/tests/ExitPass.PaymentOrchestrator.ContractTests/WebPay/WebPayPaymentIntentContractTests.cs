using System.Text.Json;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using Xunit;

namespace ExitPass.PaymentOrchestrator.ContractTests.WebPay;

/// <summary>
/// Contract tests for the provider-neutral WebPay payment intent API shape.
/// </summary>
public sealed class WebPayPaymentIntentContractTests
{
    /// <summary>
    /// Verifies the WebPay payment intent request keeps ticketReference as a first-class source-neutral field.
    /// </summary>
    [Fact]
    public void WebPayPaymentIntent_Request_UsesProviderNeutralTicketReferenceShape()
    {
        var request = new WebPayPaymentIntentRequest
        {
            SiteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            VendorSystemId = "HIKCENTRAL",
            TicketReference = "TICKET-001",
            PaymentMethod = "QRPH",
            PreferredProviderCode = "AUB",
            CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333")
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"ticketReference\":\"TICKET-001\"", json);
        Assert.Contains("\"paymentMethod\":\"QRPH\"", json);
        Assert.Contains("\"preferredProviderCode\":\"AUB\"", json);
        Assert.DoesNotContain("qrScan", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the WebPay response contains provider-neutral handoff data without raw provider DTO fields.
    /// </summary>
    [Fact]
    public void WebPayPaymentIntent_Response_UsesProviderNeutralHandoffShape()
    {
        var response = new WebPayPaymentIntentResponse
        {
            PaymentAttemptId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ParkingSessionId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TariffSnapshotId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            AmountMinorUnits = 10000,
            Currency = "PHP",
            PaymentMethod = "QRPH",
            SelectedProviderCode = "AUB",
            FallbackProviderCode = "PAYMONGO",
            RoutingReason = "PRIMARY_PROVIDER",
            Status = "PENDING_PROVIDER",
            Handoff = new WebPayPaymentHandoffDto
            {
                Type = "Redirect",
                HandoffUrl = "https://payments.test/handoff",
                QrCodeUrl = "qr-test",
                ExpiresAt = DateTimeOffset.Parse("2026-05-16T12:00:00Z")
            },
            CorrelationId = Guid.Parse("77777777-7777-7777-7777-777777777777")
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"selectedProviderCode\":\"AUB\"", json);
        Assert.Contains("\"fallbackProviderCode\":\"PAYMONGO\"", json);
        Assert.Contains("\"handoffUrl\":\"https://payments.test/handoff\"", json);
        Assert.DoesNotContain("merchantReferenceNumber", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerProduct", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawResponse", json, StringComparison.OrdinalIgnoreCase);
    }
}
