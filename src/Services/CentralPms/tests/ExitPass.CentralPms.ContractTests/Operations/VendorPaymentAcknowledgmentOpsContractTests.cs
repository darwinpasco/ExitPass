using System.Text.Json;
using ExitPass.CentralPms.Contracts.Operations;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Operations;

/// <summary>
/// Verifies the ops-facing Vendor PMS payment acknowledgment monitoring contract.
/// </summary>
public sealed class VendorPaymentAcknowledgmentOpsContractTests
{
    [Fact]
    public void SearchRequest_UsesExpectedJsonShape()
    {
        var request = new VendorPaymentAcknowledgmentSearchRequest
        {
            AcknowledgmentStatus = "RETRY_PENDING",
            VendorSystemCode = "HIKCENTRAL",
            PaymentAttemptId = Guid.Parse("279d1000-0000-0000-0000-000000000001"),
            PaymentConfirmationId = Guid.Parse("279d1000-0000-0000-0000-000000000002"),
            ParkingSessionId = Guid.Parse("279d1000-0000-0000-0000-000000000003"),
            TicketNumber = "TICKET-279D",
            CardNum = "CARD-279D",
            CorrelationId = Guid.Parse("279d1000-0000-0000-0000-000000000004"),
            CreatedFrom = DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
            CreatedTo = DateTimeOffset.Parse("2026-06-19T23:59:59Z"),
            LastAttemptedFrom = DateTimeOffset.Parse("2026-06-19T01:00:00Z"),
            LastAttemptedTo = DateTimeOffset.Parse("2026-06-19T02:00:00Z"),
            NextRetryDueOnly = true,
            PageIndex = 0,
            PageSize = 25
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("acknowledgmentStatus", out _).Should().BeTrue();
        root.TryGetProperty("vendorSystemCode", out _).Should().BeTrue();
        root.TryGetProperty("paymentAttemptId", out _).Should().BeTrue();
        root.TryGetProperty("paymentConfirmationId", out _).Should().BeTrue();
        root.TryGetProperty("parkingSessionId", out _).Should().BeTrue();
        root.TryGetProperty("ticketNumber", out _).Should().BeTrue();
        root.TryGetProperty("cardNum", out _).Should().BeTrue();
        root.TryGetProperty("correlationId", out _).Should().BeTrue();
        root.TryGetProperty("createdFrom", out _).Should().BeTrue();
        root.TryGetProperty("createdTo", out _).Should().BeTrue();
        root.TryGetProperty("lastAttemptedFrom", out _).Should().BeTrue();
        root.TryGetProperty("lastAttemptedTo", out _).Should().BeTrue();
        root.TryGetProperty("nextRetryDueOnly", out _).Should().BeTrue();
        root.TryGetProperty("pageIndex", out _).Should().BeTrue();
        root.TryGetProperty("pageSize", out _).Should().BeTrue();
    }

    [Fact]
    public void SearchResponse_UsesExpectedJsonShape()
    {
        var response = new VendorPaymentAcknowledgmentSearchResponse
        {
            Items =
            [
                new VendorPaymentAcknowledgmentSummary
                {
                    VendorPaymentAcknowledgmentId = Guid.Parse("279d2000-0000-0000-0000-000000000001"),
                    PaymentAttemptId = Guid.Parse("279d2000-0000-0000-0000-000000000002"),
                    PaymentConfirmationId = Guid.Parse("279d2000-0000-0000-0000-000000000003"),
                    ParkingSessionId = Guid.Parse("279d2000-0000-0000-0000-000000000004"),
                    VendorSystemCode = "HIKCENTRAL",
                    VendorSessionRef = "HIK:CARD-279D",
                    TicketNumber = "TICKET-279D",
                    CardNum = "CARD-279D",
                    AcknowledgmentStatus = "RETRY_PENDING",
                    StatusBucket = "retry_pending",
                    VendorCode = "128",
                    VendorMessage = "Vendor retry pending.",
                    RequestFeeMinorUnits = 5000,
                    RequestCurrencyCode = "PHP",
                    ConfirmedFeeMinorUnits = null,
                    VendorConfirmedAt = null,
                    AttemptCount = 2,
                    LastAttemptedAt = DateTimeOffset.Parse("2026-06-19T01:00:00Z"),
                    NextRetryAt = DateTimeOffset.Parse("2026-06-19T01:05:00Z"),
                    CorrelationId = Guid.Parse("279d2000-0000-0000-0000-000000000005"),
                    CreatedAt = DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2026-06-19T01:00:00Z")
                }
            ],
            StatusBuckets = new VendorPaymentAcknowledgmentStatusBuckets
            {
                Pending = 1,
                RetryPending = 2,
                Failed = 3,
                Confirmed = 4,
                SkippedDisabled = 5,
                Cancelled = 6
            },
            PageIndex = 0,
            PageSize = 25,
            HasMore = true
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));
        var root = document.RootElement;
        var item = root.GetProperty("items")[0];

        root.TryGetProperty("items", out _).Should().BeTrue();
        root.TryGetProperty("statusBuckets", out _).Should().BeTrue();
        root.TryGetProperty("pageIndex", out _).Should().BeTrue();
        root.TryGetProperty("pageSize", out _).Should().BeTrue();
        root.TryGetProperty("hasMore", out _).Should().BeTrue();

        item.TryGetProperty("vendorPaymentAcknowledgmentId", out _).Should().BeTrue();
        item.TryGetProperty("paymentAttemptId", out _).Should().BeTrue();
        item.TryGetProperty("paymentConfirmationId", out _).Should().BeTrue();
        item.TryGetProperty("parkingSessionId", out _).Should().BeTrue();
        item.TryGetProperty("vendorSystemCode", out _).Should().BeTrue();
        item.TryGetProperty("vendorSessionRef", out _).Should().BeTrue();
        item.TryGetProperty("ticketNumber", out _).Should().BeTrue();
        item.TryGetProperty("cardNum", out _).Should().BeTrue();
        item.TryGetProperty("acknowledgmentStatus", out _).Should().BeTrue();
        item.TryGetProperty("statusBucket", out _).Should().BeTrue();
        item.TryGetProperty("vendorCode", out _).Should().BeTrue();
        item.TryGetProperty("vendorMessage", out _).Should().BeTrue();
        item.TryGetProperty("requestFeeMinorUnits", out _).Should().BeTrue();
        item.TryGetProperty("requestCurrencyCode", out _).Should().BeTrue();
        item.TryGetProperty("confirmedFeeMinorUnits", out _).Should().BeTrue();
        item.TryGetProperty("vendorConfirmedAt", out _).Should().BeTrue();
        item.TryGetProperty("attemptCount", out _).Should().BeTrue();
        item.TryGetProperty("lastAttemptedAt", out _).Should().BeTrue();
        item.TryGetProperty("nextRetryAt", out _).Should().BeTrue();
        item.TryGetProperty("correlationId", out _).Should().BeTrue();
        item.TryGetProperty("createdAt", out _).Should().BeTrue();
        item.TryGetProperty("updatedAt", out _).Should().BeTrue();
    }

    [Fact]
    public void VendorPaymentAcknowledgmentOps_DoesNotExposeSecretBearingFields()
    {
        var contractNames = new[]
        {
            typeof(VendorPaymentAcknowledgmentSearchRequest),
            typeof(VendorPaymentAcknowledgmentSearchResponse),
            typeof(VendorPaymentAcknowledgmentDetailResponse),
            typeof(VendorPaymentAcknowledgmentSummary),
            typeof(VendorPaymentAcknowledgmentStatusBuckets),
            typeof(VendorPaymentAcknowledgmentDiagnosticDto)
        }
        .SelectMany(type => type.GetMembers().Select(member => member.Name).Append(type.Name));

        contractNames.Should().NotContain(name => name.Contains("AppSecret", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Signature", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("AuthHeader", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Payload", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
}
