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

    /// <summary>
    /// Verifies the WebPay pre-payment parking-session response carries resolved site context for payment intent creation.
    /// </summary>
    [Fact]
    public void WebPayParkingSessionResolve_Response_IncludesResolvedSessionContext()
    {
        var response = new WebPayParkingSessionResolveResponse
        {
            ParkingSessionId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TariffSnapshotId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            SiteGroupId = Guid.Parse("29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"),
            SiteId = Guid.Parse("93bd3cb3-e806-4c5c-ac8c-df6c4addff14"),
            VendorSystemId = "45a625de-9034-4fb6-b527-0950d384e51f",
            SiteGroupName = "WebPay Test Site Group 2026-05-19",
            SiteName = "WebPay Test Site 2026-05-19",
            TicketReference = "WEBPAY-20260519-FRESH-001",
            PlateNumber = "WEBPAY001",
            EntryTime = DateTimeOffset.Parse("2026-05-19T02:01:00+08:00"),
            AmountMinorUnits = 10000,
            Currency = "PHP",
            ParkingStatus = "PaymentRequired",
            PaymentStatus = "Not Started",
            FeeValidUntil = DateTimeOffset.Parse("2026-05-19T15:59:59Z"),
            CorrelationId = Guid.Parse("77777777-7777-7777-7777-777777777777")
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"siteGroupId\":\"29b8b4f4-40dd-447b-ac06-dd52e6ad51c5\"", json);
        Assert.Contains("\"siteId\":\"93bd3cb3-e806-4c5c-ac8c-df6c4addff14\"", json);
        Assert.Contains("\"vendorSystemId\":\"45a625de-9034-4fb6-b527-0950d384e51f\"", json);
        Assert.Contains("\"amountMinorUnits\":10000", json);
        Assert.Contains("\"currency\":\"PHP\"", json);
        Assert.Contains("\"parkingStatus\":\"PaymentRequired\"", json);
        Assert.Contains("\"paymentStatus\":\"Not Started\"", json);
        Assert.Contains("\"plateNumber\":\"WEBPAY001\"", json);
        Assert.Contains("\"entryTime\":", json);
        Assert.DoesNotContain("2030-04-01", json, StringComparison.Ordinal);
    }
}
