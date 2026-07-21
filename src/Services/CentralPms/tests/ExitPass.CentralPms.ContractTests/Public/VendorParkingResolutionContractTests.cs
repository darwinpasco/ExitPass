using System.Text.Json;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Contracts.Public.VendorParking;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Public;

/// <summary>
/// Verifies the public Central PMS vendor parking resolution API contract shape.
/// </summary>
public sealed class VendorParkingResolutionContractTests
{
    /// <summary>
    /// Verifies provider-neutral request JSON field names.
    /// </summary>
    [Fact]
    public void ResolveVendorParking_request_uses_provider_neutral_json_shape()
    {
        var request = new ResolveVendorParkingRequest
        {
            SiteGroupId = "SG-001",
            SiteId = "SITE-001",
            VendorSystemId = "FAKE-PMS",
            PlateNumber = "ABC1234",
            TicketReference = null,
            CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("siteGroupId", out _).Should().BeTrue();
        root.TryGetProperty("siteId", out _).Should().BeTrue();
        root.TryGetProperty("vendorSystemId", out _).Should().BeTrue();
        root.TryGetProperty("plateNumber", out _).Should().BeTrue();
        root.TryGetProperty("ticketReference", out _).Should().BeTrue();
        root.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    /// <summary>
    /// Verifies provider-neutral response JSON field names.
    /// </summary>
    [Fact]
    public void ResolveVendorParking_response_uses_provider_neutral_json_shape()
    {
        var response = new ResolveVendorParkingResponse
        {
            ParkingSessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            TariffSnapshotId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SiteGroupId = "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
            SiteId = "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
            SiteGroupName = "WebPay Test Site Group 2026-05-19",
            SiteName = "WebPay Test Site 2026-05-19",
            LookupOutcome = "resolved",
            PlateNumber = "ABC1234",
            TicketReference = null,
            EntryTime = new DateTimeOffset(2026, 5, 19, 2, 1, 0, TimeSpan.FromHours(8)),
            CurrentFeeCalculationTime = new DateTimeOffset(2026, 5, 19, 8, 0, 0, TimeSpan.FromHours(8)),
            NetPayableMinorUnits = 10000,
            Currency = "PHP",
            TariffExpiresAt = new DateTimeOffset(2026, 5, 19, 15, 59, 59, TimeSpan.Zero),
            FeeValidUntil = new DateTimeOffset(2026, 5, 19, 15, 59, 59, TimeSpan.Zero),
            ParkingStatus = "PaymentRequired",
            PaymentStatus = "Not Started",
            StatutoryDiscountApplied = true,
            StatutoryDiscountValidationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            StatutoryDiscountApplicationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            StatutoryDiscountDecisionCommandId = Guid.Parse("edededed-eded-4ded-8ded-edededededed"),
            OriginalTariffSnapshotId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            EffectiveTariffSnapshotId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            AppliedTariffSnapshotId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            PolicyResolutionBasis = "NATIONAL_LAW_FALLBACK",
            StatutoryDiscountPolicyReferenceId = Guid.Parse("abababab-abab-4bab-8bab-abababababab"),
            BenefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT",
            StatutoryDiscountEntitlementType = "PWD",
            StatutoryDiscountAmountMinorUnits = 2232,
            StatutoryDiscountFinalPayableMinorUnits = 8929,
            StatutoryDiscountDecisionTimestamp = new DateTimeOffset(2026, 5, 19, 8, 1, 0, TimeSpan.Zero),
            VendorSystemId = "FAKE-PMS",
            CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("parkingSessionId", out _).Should().BeTrue();
        root.TryGetProperty("tariffSnapshotId", out _).Should().BeTrue();
        root.TryGetProperty("siteGroupId", out _).Should().BeTrue();
        root.TryGetProperty("siteId", out _).Should().BeTrue();
        root.TryGetProperty("siteGroupName", out _).Should().BeTrue();
        root.TryGetProperty("siteName", out _).Should().BeTrue();
        root.TryGetProperty("lookupOutcome", out _).Should().BeTrue();
        root.TryGetProperty("plateNumber", out _).Should().BeTrue();
        root.TryGetProperty("ticketReference", out _).Should().BeTrue();
        root.TryGetProperty("entryTime", out _).Should().BeTrue();
        root.TryGetProperty("currentFeeCalculationTime", out _).Should().BeTrue();
        root.TryGetProperty("netPayableMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("currency", out _).Should().BeTrue();
        root.TryGetProperty("tariffExpiresAt", out _).Should().BeTrue();
        root.TryGetProperty("feeValidUntil", out _).Should().BeTrue();
        root.GetProperty("feeValidUntil").GetString().Should().NotStartWith("2030-04-01");
        root.TryGetProperty("parkingStatus", out _).Should().BeTrue();
        root.TryGetProperty("paymentStatus", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountApplied", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountValidationId", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountApplicationId", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountDecisionCommandId", out _).Should().BeTrue();
        root.TryGetProperty("originalTariffSnapshotId", out _).Should().BeTrue();
        root.TryGetProperty("effectiveTariffSnapshotId", out _).Should().BeTrue();
        root.TryGetProperty("appliedTariffSnapshotId", out _).Should().BeTrue();
        root.TryGetProperty("policyResolutionBasis", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountPolicyReferenceId", out _).Should().BeTrue();
        root.TryGetProperty("benefitType", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountEntitlementType", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountAmountMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountFinalPayableMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("statutoryDiscountDecisionTimestamp", out _).Should().BeTrue();
        root.TryGetProperty("vendorSystemId", out _).Should().BeTrue();
        root.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that HikCentral-specific fields do not appear in Central PMS vendor parking contracts.
    /// </summary>
    [Fact]
    public void ResolveVendorParking_DoesNotExposeHikCentralFields()
    {
        var contractNames = new[]
        {
            typeof(ResolveVendorParkingRequest),
            typeof(ResolveVendorParkingResponse)
        }
        .SelectMany(type => type.GetMembers().Select(member => member.Name).Append(type.Name));

        contractNames.Should().NotContain(name => name.Contains("HikCentral", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Ak", StringComparison.OrdinalIgnoreCase));
        contractNames.Should().NotContain(name => name.Contains("Sk", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies production Central PMS assemblies do not introduce an in-memory resolved-parking store.
    /// </summary>
    [Fact]
    public void VendorResolveThenCreatePaymentAttempt_DoesNotUseInMemoryResolvedParkingStore()
    {
        var productionTypes = new[]
        {
            typeof(Program).Assembly,
            typeof(IVendorParkingResolutionPersistence).Assembly,
            typeof(VendorParkingResolutionPersistence).Assembly
        }
        .SelectMany(assembly => assembly.GetTypes())
        .Select(type => type.FullName ?? type.Name);

        productionTypes.Should().NotContain(name =>
            name.Contains("InMemoryResolvedParking", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ResolvedParkingStore", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }
}
