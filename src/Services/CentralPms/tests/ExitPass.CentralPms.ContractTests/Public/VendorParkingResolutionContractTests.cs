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
            LookupOutcome = "resolved",
            PlateNumber = "ABC1234",
            TicketReference = null,
            NetPayableMinorUnits = 10000,
            Currency = "PHP",
            TariffExpiresAt = new DateTimeOffset(2026, 4, 1, 1, 45, 0, TimeSpan.Zero),
            VendorSystemId = "FAKE-PMS",
            CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));
        var root = document.RootElement;

        root.TryGetProperty("parkingSessionId", out _).Should().BeTrue();
        root.TryGetProperty("tariffSnapshotId", out _).Should().BeTrue();
        root.TryGetProperty("lookupOutcome", out _).Should().BeTrue();
        root.TryGetProperty("plateNumber", out _).Should().BeTrue();
        root.TryGetProperty("ticketReference", out _).Should().BeTrue();
        root.TryGetProperty("netPayableMinorUnits", out _).Should().BeTrue();
        root.TryGetProperty("currency", out _).Should().BeTrue();
        root.TryGetProperty("tariffExpiresAt", out _).Should().BeTrue();
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
