using System.Text.Json;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementStatutoryBenefitReviewContractTests
{
    [Theory]
    [InlineData("ManagementPlatformStatutoryBenefitReviewList", "statutory-discounts.review.queue.read")]
    [InlineData("ManagementPlatformStatutoryBenefitReviewDetail", "statutory-discounts.review.detail.read")]
    [InlineData("ManagementPlatformStatutoryBenefitReviewEvidence", "statutory-discounts.evidence.review.view")]
    [InlineData("ManagementPlatformStatutoryBenefitReviewDecision", "statutory-discounts.decision.approve")]
    [InlineData("ManagementPlatformStatutoryBenefitReviewDecision", "statutory-discounts.decision.reject")]
    public void PoliciesMapToExistingNarrowPermissions(string policy, string permission)
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(policy).Should().Contain(permission);
    }

    [Fact]
    public void ReadContractsExcludePlateStorageAndAuthorityMaterial()
    {
        var names = new[]
        {
            typeof(ManagementStatutoryBenefitReviewQueueItem),
            typeof(ManagementStatutoryBenefitReviewDetail),
            typeof(ManagementStatutoryBenefitEvidenceItem)
        }.SelectMany(type => type.GetProperties()).Select(property => property.Name).ToArray();

        names.Should().NotContain(name => name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContractDeclaresCentralPmsOnlyPhpAndTerminalDecisionIntegrity()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "..", "..", "contracts", "management-platform", "statutory-benefit-review-api.v1.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        root.GetProperty("contractId").GetString().Should().Be(ManagementStatutoryBenefitReviewValues.ContractVersion);
        root.GetProperty("owner").GetString().Should().Be("Central PMS");
        root.GetProperty("currency").GetProperty("supported")[0].GetString().Should().Be("PHP");
        root.GetProperty("authority").GetProperty("directClientConnections").GetBoolean().Should().BeFalse();
        root.GetProperty("decisionIntegrity").GetProperty("terminalStatesImmutable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DecisionRequestContainsNoClientAuthoredActorOrScope()
    {
        var names = typeof(ManagementStatutoryBenefitDecisionCommand).GetProperties().Select(property => property.Name).ToArray();
        names.Should().NotContain("ReviewerUserId");
        names.Should().NotContain("SiteReference");
        names.Should().NotContain("SiteGroupReference");
        names.Should().NotContain("Role");
        names.Should().NotContain("Permission");
    }
}
