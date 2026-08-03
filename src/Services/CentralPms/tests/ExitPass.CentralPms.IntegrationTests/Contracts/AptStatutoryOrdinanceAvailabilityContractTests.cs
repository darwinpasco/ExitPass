using System.Text.Json;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Contracts;

public sealed class AptStatutoryOrdinanceAvailabilityContractTests
{
    [Fact]
    public void ContractDtos_MapAptSafeFieldsWithoutPolicyAdministrationOrSecrets()
    {
        var request = new AptStatutoryOrdinanceAvailabilityRequest(
            SiteGroupId: Guid.NewGuid().ToString("D"),
            SiteId: Guid.NewGuid().ToString("D"),
            TerminalId: "APT-TERMINAL-001",
            VendorSystemId: "FAKE-PMS",
            ParkingSessionId: Guid.NewGuid().ToString("D"),
            TicketReference: null,
            PlateNumber: null,
            EntitlementType: "SENIOR_CITIZEN",
            CorrelationId: Guid.NewGuid());
        var response = new AptStatutoryOrdinanceAvailabilityResponse(
            Operation: "REVALIDATE",
            RevalidationOutcome: "PASSED_UNCHANGED",
            Classification: "AVAILABLE",
            EntitlementType: request.EntitlementType,
            OrdinanceCoverageAvailable: true,
            StatutoryRequestAllowed: true,
            PreCashRevalidationPassed: true,
            ReadyForStatutoryCashFlow: true,
            OrdinaryPaymentPreserved: true,
            ParkingSessionId: Guid.Parse(request.ParkingSessionId!),
            SiteId: Guid.Parse(request.SiteId),
            SiteGroupId: Guid.Parse(request.SiteGroupId),
            ResolvedScopeType: "SITE",
            CoverageClassification: "ACTIVE_COVERED",
            PolicyStatusClassification: "ACTIVE",
            EffectiveFrom: DateOnly.Parse("2026-01-01"),
            EffectiveTo: null,
            AuthorityClassification: "CENTRAL_PMS_READ_MODEL",
            JurisdictionDisplayName: "SYNTHETIC_LGU",
            SupportReference: "SAFE-POLICY-REF",
            CorrelationId: request.CorrelationId,
            EvaluatedAt: DateTimeOffset.Parse("2026-08-03T02:30:00Z"),
            AuthoritativeUpdatedAt: DateTimeOffset.Parse("2026-08-03T02:00:00Z"),
            Retryable: false,
            SafeMessage: "Statutory ordinance coverage remains available.");

        var json = JsonSerializer.Serialize(new { request, response }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var lower = json.ToLowerInvariant();

        lower.Should().Contain("statutoryrequestallowed");
        lower.Should().Contain("precashrevalidationpassed");
        lower.Should().Contain("ordinarypaymentpreserved");
        lower.Should().NotContain("password");
        lower.Should().NotContain("apikey");
        lower.Should().NotContain("connectionstring");
        lower.Should().NotContain("authorization");
        lower.Should().NotContain("hikcentral");
        lower.Should().NotContain("reviewer");
        lower.Should().NotContain("evidencehash");
    }

    [Fact]
    public void AptOrdinanceAvailabilityPolicy_MapsToNarrowAptPermission()
    {
        AptStatutoryOrdinanceAvailabilityEndpoints.ReadPolicy.Should().Be(AptStatutoryOrdinanceAvailabilityValues.PolicyName);
        CentralPmsRbacPolicyCatalog.ResolvePermissions(AptStatutoryOrdinanceAvailabilityEndpoints.ReadPolicy)
            .Should()
            .BeEquivalentTo([AptStatutoryOrdinanceAvailabilityValues.Permission]);
    }

    [Fact]
    public void MachineReadableContract_DeclaresReadOnlyAptRoutes()
    {
        var path = ResolveRepositoryPath(
            "contracts",
            "central-pms",
            "apt-statutory-ordinance-availability.v1.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        root.GetProperty("routes")[0].GetProperty("path").GetString()
            .Should()
            .Be("/v1/apt/statutory-discounts/ordinance-availability/resolve");
        root.GetProperty("routes")[1].GetProperty("path").GetString()
            .Should()
            .Be("/v1/apt/statutory-discounts/ordinance-availability/revalidate");
        root.GetProperty("authorization").GetProperty("policy").GetString()
            .Should()
            .Be(AptStatutoryOrdinanceAvailabilityValues.PolicyName);
        root.GetProperty("authorization").GetProperty("humanUserAllowed").GetBoolean()
            .Should()
            .BeFalse();
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
