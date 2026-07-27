using System.Text.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Contracts;

/// <summary>
/// Contract checks for the APT payable-basis readiness facade.
/// </summary>
public sealed class AptPayableBasisReadinessContractTests
{
    [Fact]
    public void ResolveAndRevalidateContracts_MapAptSafeFieldsWithoutProviderCredentials()
    {
        var resolve = new AptPayableBasisResolveRequest(
            SiteGroupId: Guid.NewGuid().ToString("D"),
            SiteId: Guid.NewGuid().ToString("D"),
            SitePosServerId: Guid.NewGuid().ToString("D"),
            TerminalId: "APT-TERMINAL-001",
            VendorSystemId: "FAKE-PMS",
            ReferenceType: "TICKET",
            TicketReference: "TICKET-001",
            PlateNumber: null,
            CorrelationId: Guid.NewGuid());
        var revalidate = new AptPayableBasisRevalidateRequest(
            ParkingSessionId: Guid.NewGuid().ToString("D"),
            TariffSnapshotId: Guid.NewGuid().ToString("D"),
            SiteGroupId: resolve.SiteGroupId,
            SiteId: resolve.SiteId,
            SitePosServerId: resolve.SitePosServerId,
            TerminalId: resolve.TerminalId,
            VendorSystemId: resolve.VendorSystemId,
            TicketReference: resolve.TicketReference,
            PlateNumber: null,
            ExpectedAmountMinorUnits: 10000,
            ExpectedCurrency: "PHP",
            CorrelationId: resolve.CorrelationId);
        var response = new AptPayableBasisReadinessResponse(
            Operation: "REVALIDATE",
            RevalidationOutcome: "PASSED_UNCHANGED",
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteGroupId: Guid.Parse(resolve.SiteGroupId),
            SiteId: Guid.Parse(resolve.SiteId),
            SitePosServerId: Guid.Parse(resolve.SitePosServerId),
            TerminalId: resolve.TerminalId,
            SiteGroupName: "Parking Group",
            SiteName: "Parking Site",
            TicketReference: resolve.TicketReference,
            PlateNumber: "ABC1234",
            EntryTimestamp: DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
            ParkingStatus: "PaymentRequired",
            PaymentStatus: "Not Started",
            AuthoritativeAmountMinorUnits: 10000,
            Currency: "PHP",
            TariffCalculatedAt: DateTimeOffset.Parse("2026-07-27T00:01:00Z"),
            TariffValidUntil: DateTimeOffset.Parse("2026-07-27T00:06:00Z"),
            FeeValidUntil: DateTimeOffset.Parse("2026-07-27T00:06:00Z"),
            VendorSystemId: "FAKE-PMS",
            ReadinessDimensions:
            [
                new AptReadinessDimensionDto("sessionReadiness", "READY", true, null, false, "Session is active.")
            ],
            SessionReadiness: "READY",
            TariffReadiness: "READY",
            PaymentEligibility: "READY",
            TerminalCashAvailability: "READY",
            FiscalReadiness: "READY",
            SalesInvoiceConfigurationReadiness: "READY",
            CashAcceptanceReadiness: "READY",
            ReadyForCashAcceptance: true,
            BlockingReasonCodes: [],
            Retryable: false,
            SafeUserFacingClassification: "READY_FOR_CASH_ACCEPTANCE",
            CorrelationId: resolve.CorrelationId);

        var json = JsonSerializer.Serialize(new { resolve, revalidate, response }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("terminalId");
        json.Should().Contain("authoritativeAmountMinorUnits");
        json.Should().Contain("readyForCashAcceptance");
        json.Should().Contain("revalidationOutcome");
        var lowerJson = json.ToLowerInvariant();
        lowerJson.Should().NotContain("apikey");
        lowerJson.Should().NotContain("connectionstring");
        lowerJson.Should().NotContain("authorization");
        lowerJson.Should().NotContain("hikcentral");
    }

    [Fact]
    public void AptPayableBasisPolicy_MapsToNarrowTerminalCashPermission()
    {
        AptPayableBasisEndpoints.ReadPolicy.Should().Be("TerminalCashPayableBasisRead");
        CentralPmsRbacPolicyCatalog.ResolvePermissions(AptPayableBasisEndpoints.ReadPolicy)
            .Should()
            .BeEquivalentTo(["terminal-cash.payable-basis.read"]);
    }

    [Fact]
    public void MachineReadableContract_DeclaresRoutesAndSideEffectBoundaries()
    {
        var path = ResolveRepositoryPath(
            "contracts",
            "central-pms",
            "apt-session-payable-basis-readiness.v1.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        root.GetProperty("routes")[0].GetProperty("path").GetString()
            .Should()
            .Be("/v1/terminal-cash-payments/payable-basis/resolve");
        root.GetProperty("routes")[1].GetProperty("path").GetString()
            .Should()
            .Be("/v1/terminal-cash-payments/payable-basis/revalidate");
        root.GetProperty("authorization").GetProperty("policy").GetString()
            .Should()
            .Be("TerminalCashPayableBasisRead");
        root.GetProperty("authorityBoundaries").GetProperty("noDirectHikCentralAccess").GetBoolean()
            .Should()
            .BeTrue();
        root.GetProperty("sideEffects").EnumerateObject()
            .Should()
            .OnlyContain(property => property.Value.ValueKind == JsonValueKind.False);
    }

    private static string ResolveRepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
