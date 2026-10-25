using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class AptCashCustodyMovementAuthorizationPatchTests
{
    [Fact]
    public void Patch_UsesExplicitRolesAndDoesNotGrantTheBaseCashierRole()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "infra", "db", "patches", "ExitPass_CentralPms_AptCashCustodyMovementAuthorization_v1.3.sql"));

        source.Should().Contain("APT_CASH_CUSTODY_MOVEMENT_OPERATOR");
        source.Should().Contain("APT_CASH_CUSTODY_SUPERVISOR");
        source.Should().Contain("APT_SITE_CUSTODIAN");
        source.Should().Contain("APT_CASH_COLLECTOR");
        source.Should().NotContain("('APT_CASHIER_OPERATOR', 'cash-custody-movements.");
        source.Should().Contain("ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE");
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
