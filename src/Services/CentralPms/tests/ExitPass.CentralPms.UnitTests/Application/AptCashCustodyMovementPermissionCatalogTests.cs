using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class AptCashCustodyMovementPermissionCatalogTests
{
    [Fact]
    public void MovementPermissions_AreExplicitAndNotInheritedFromCashierOperations()
    {
        AptHumanPermissionCatalog.CashCustodyMovementPermissions.Should().Equal(
            "cash-custody-movements.initiate",
            "cash-custody-movements.acknowledge",
            "cash-custody-movements.correct",
            "cash-custody-movements.read");

        AptHumanPermissionCatalog.OperationalPermissions.Should().NotIntersectWith(
            AptHumanPermissionCatalog.CashCustodyMovementPermissions);
    }
}
