using System.Text.Json;
using ExitPass.CentralPms.Contracts.Operations;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Operations;

/// <summary>
/// Verifies the ops-facing ticket session summary contract.
/// </summary>
public sealed class TicketSessionSummaryContractTests
{
    /// <summary>
    /// Verifies request JSON field names.
    /// </summary>
    [Fact]
    public void TicketSessionSummary_request_uses_provider_neutral_json_shape()
    {
        var request = new TicketSessionSummaryRequest
        {
            TicketNumber = "TICKET-275",
            CardNum = null,
            SiteId = Guid.Parse("27520000-0000-0000-0000-000000000001"),
            SiteGroupId = Guid.Parse("27520000-0000-0000-0000-000000000002"),
            CorrelationId = Guid.Parse("27520000-0000-0000-0000-000000000003")
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("ticketNumber", out _).Should().BeTrue();
        root.TryGetProperty("cardNum", out _).Should().BeTrue();
        root.TryGetProperty("siteId", out _).Should().BeTrue();
        root.TryGetProperty("siteGroupId", out _).Should().BeTrue();
        root.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    /// <summary>
    /// Verifies response JSON field names.
    /// </summary>
    [Fact]
    public void TicketSessionSummary_response_uses_provider_neutral_json_shape()
    {
        var response = new TicketSessionSummaryResponse
        {
            TicketNumber = "TICKET-275",
            CardNum = "TICKET-275",
            PlateLicense = "Unknown",
            ParkingInTime = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero),
            ParkingDurationSeconds = 7200,
            FeeMinorUnits = 12550,
            CurrencyCode = "PHP",
            FeeRuleType = null,
            FeeRuleIndexCode = "RULE-001",
            FeeRuleName = "Standard parking",
            VendorSessionStatus = "PAYMENT_REQUIRED",
            VendorSystemCode = "FAKE_PMS",
            VendorConfirmationCode = "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE",
            VendorMessage = "Vendor session and tariff summary resolved.",
            ParkingSessionId = Guid.Parse("27520000-0000-0000-0000-000000000004"),
            PaymentAttemptId = Guid.Parse("27520000-0000-0000-0000-000000000005"),
            PaymentAttemptStatus = "FINALIZED",
            PaymentStatus = "Paid",
            PaymentConfirmationStatus = "RECORDED",
            VendorConfirmationStatus = null,
            VendorConfirmationTimestamp = null,
            Diagnostics =
            [
                new TicketSessionSummaryDiagnosticDto(
                    "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE",
                    "Unavailable",
                    "central-pms-read-model",
                    Retryable: false,
                    VendorSystemCode: "FAKE_PMS",
                    VendorConfirmationCode: "VENDOR_CONFIRMATION_STATUS_UNAVAILABLE",
                    VendorMessage: "Unavailable",
                    CorrelationId: Guid.Parse("27520000-0000-0000-0000-000000000003"))
            ],
            CorrelationId = Guid.Parse("27520000-0000-0000-0000-000000000003")
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("ticketNumber", out _).Should().BeTrue();
        root.TryGetProperty("cardNum", out _).Should().BeTrue();
        root.TryGetProperty("plateLicense", out _).Should().BeTrue();
        root.TryGetProperty("parkingInTime", out _).Should().BeTrue();
        root.TryGetProperty("parkingDurationSeconds", out _).Should().BeTrue();
        root.TryGetProperty("feeMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("currencyCode", out _).Should().BeTrue();
        root.TryGetProperty("feeRuleType", out _).Should().BeTrue();
        root.TryGetProperty("feeRuleIndexCode", out _).Should().BeTrue();
        root.TryGetProperty("feeRuleName", out _).Should().BeTrue();
        root.TryGetProperty("vendorSessionStatus", out _).Should().BeTrue();
        root.TryGetProperty("vendorSystemCode", out _).Should().BeTrue();
        root.TryGetProperty("vendorConfirmationCode", out _).Should().BeTrue();
        root.TryGetProperty("vendorMessage", out _).Should().BeTrue();
        root.TryGetProperty("parkingSessionId", out _).Should().BeTrue();
        root.TryGetProperty("paymentAttemptId", out _).Should().BeTrue();
        root.TryGetProperty("paymentAttemptStatus", out _).Should().BeTrue();
        root.TryGetProperty("paymentStatus", out _).Should().BeTrue();
        root.TryGetProperty("paymentConfirmationStatus", out _).Should().BeTrue();
        root.TryGetProperty("vendorConfirmationStatus", out _).Should().BeTrue();
        root.TryGetProperty("vendorConfirmationTimestamp", out _).Should().BeTrue();
        root.TryGetProperty("diagnostics", out _).Should().BeTrue();
        root.TryGetProperty("correlationId", out _).Should().BeTrue();

        var diagnostic = root.GetProperty("diagnostics")[0];
        diagnostic.TryGetProperty("vendorSystemCode", out _).Should().BeTrue();
        diagnostic.TryGetProperty("vendorConfirmationCode", out _).Should().BeTrue();
        diagnostic.TryGetProperty("vendorMessage", out _).Should().BeTrue();
        diagnostic.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    /// <summary>
    /// Verifies HikCentral-specific fields are not exposed.
    /// </summary>
    [Fact]
    public void TicketSessionSummary_DoesNotExposeHikCentralFields()
    {
        var contractNames = new[]
        {
            typeof(TicketSessionSummaryRequest),
            typeof(TicketSessionSummaryResponse),
            typeof(TicketSessionSummaryDiagnosticDto)
        }
        .SelectMany(type => type.GetMembers().Select(member => member.Name).Append(type.Name));

        contractNames.Should().NotContain(name => name.Contains("HikCentral", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Ak", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Sk", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
}
