using System.Text.Json;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using Xunit;

namespace ExitPass.VendorPmsAdapter.ContractTests.Parking;

/// <summary>
/// Contract tests for provider-neutral Vendor PMS parking DTOs.
/// </summary>
public sealed class VendorParkingContractsTests
{
    /// <summary>
    /// Verifies that a successful session lookup serializes with provider-neutral field names.
    /// </summary>
    [Fact]
    public void SessionLookupResponse_WhenFound_SerializesProviderNeutralShape()
    {
        var correlationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var response = new VendorParkingSessionLookupResponse(
            VendorParkingLookupStatus.Found,
            new VendorParkingSessionDto(
                "HIKCENTRAL",
                "HIKCENTRAL:ABC123:20260515090000",
                "ABC123",
                DateTimeOffset.Parse("2026-05-15T09:00:00+08:00"),
                3600,
                "ACTIVE",
                new VendorTariffQuoteDto(12500, "PHP", "RULE-1", "Standard", DateTimeOffset.Parse("2026-05-15T10:00:00+08:00"))),
            null,
            false,
            correlationId);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"status\":0", json);
        Assert.Contains("\"vendorProviderCode\":\"HIKCENTRAL\"", json);
        Assert.Contains("\"plateNumber\":\"ABC123\"", json);
        Assert.Contains("\"amountMinor\":12500", json);
        Assert.DoesNotContain("plateLicense", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that not-found tariff lookup results have deterministic error semantics.
    /// </summary>
    [Fact]
    public void TariffQuoteResponse_WhenNotFound_SerializesDeterministicNotFoundShape()
    {
        var response = new VendorTariffQuoteResponse(
            VendorParkingLookupStatus.NotFound,
            null,
            "VENDOR_SESSION_NOT_FOUND",
            false,
            Guid.Parse("22222222-3333-4444-5555-666666666666"));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"status\":1", json);
        Assert.Contains("\"errorCode\":\"VENDOR_SESSION_NOT_FOUND\"", json);
        Assert.Contains("\"retryable\":false", json);
    }

    /// <summary>
    /// Verifies that confirmed parking fee responses serialize without vendor-specific field names.
    /// </summary>
    [Fact]
    public void ParkingFeeConfirmationResponse_WhenConfirmed_SerializesProviderNeutralShape()
    {
        var response = new VendorParkingFeeConfirmationResponse(
            VendorParkingLookupStatus.Confirmed,
            new VendorParkingFeeConfirmationDto(
                20000,
                "PHP",
                DateTimeOffset.Parse("2022-04-12T14:48:11+08:00")),
            null,
            false,
            Guid.Parse("33333333-4444-5555-6666-777777777777"));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"status\":6", json);
        Assert.Contains("\"amountMinor\":20000", json);
        Assert.Contains("\"feeTime\":\"2022-04-12T14:48:11+08:00\"", json);
        Assert.DoesNotContain("plateLicense", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cardNum", json, StringComparison.OrdinalIgnoreCase);
    }
}
