using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.HumanAuthentication;

public sealed class AptCashCustodyCounterpartyPolicyTests
{
    [Fact]
    public void Safe_drop_allows_supervisory_and_custodian_responsibilities_only()
    {
        AptCashCustodyCounterpartyPolicy.TryResolveEligibleRoleCodes("SafeDrop", null, out var roles).Should().BeTrue();
        roles.Should().Equal(
            ApprovedIdentityRoleCatalog.OperationsSupervisor,
            ApprovedIdentityRoleCatalog.AptCashCustodySupervisor,
            ApprovedIdentityRoleCatalog.AptSiteCustodian);
        roles.Should().NotContain(ApprovedIdentityRoleCatalog.AptCashCollector);
    }

    [Fact]
    public void Direct_remittance_allows_cash_collectors_only()
    {
        AptCashCustodyCounterpartyPolicy.TryResolveEligibleRoleCodes("DirectRemittance", null, out var roles).Should().BeTrue();
        roles.Should().Equal(ApprovedIdentityRoleCatalog.AptCashCollector);
    }

    [Theory]
    [InlineData("SafeDrop", "OPERATIONS_SUPERVISOR")]
    [InlineData("DirectRemittance", "APT_CASH_COLLECTOR")]
    public void Cash_return_follows_the_original_completed_movement_responsibility(string originalType, string expectedRole)
    {
        AptCashCustodyCounterpartyPolicy.TryResolveEligibleRoleCodes("CashReturn", originalType, out var roles).Should().BeTrue();
        roles.Should().Contain(expectedRole);
    }

    [Theory]
    [InlineData("CashReturn", null)]
    [InlineData("CashReturn", "CashReturn")]
    [InlineData("Unknown", null)]
    public void Invalid_or_ambiguous_queries_fail_closed(string movementType, string? originalType)
    {
        AptCashCustodyCounterpartyPolicy.TryResolveEligibleRoleCodes(movementType, originalType, out var roles).Should().BeFalse();
        roles.Should().BeEmpty();
    }
}
