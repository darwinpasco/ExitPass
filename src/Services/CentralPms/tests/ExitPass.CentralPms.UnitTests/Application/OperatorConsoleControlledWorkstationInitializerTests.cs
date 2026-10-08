using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleControlledWorkstationInitializerTests
{
    [Fact]
    public void ProvisioningSql_ConfiguresDeviceAuthorityWithoutReadingOrMutatingShifts()
    {
        var sql = ReadRepoFile("scripts", "v1.3", "local-runtime", "sql", "Initialize-OperatorConsoleControlledWorkstation.sql");

        sql.Should().Contain("operator_device_bindings");
        sql.Should().Contain("operator_device_assignment_history");
        sql.Should().Contain("identity.user_role_scope_grants");
        sql.Should().Contain("browser_key_thumbprint = v_proof_hash");
        sql.Should().Contain("last_seen_at = now()");
        sql.Should().Contain("last_seen_at <= now() - interval '12 hours'");
        sql.Should().NotContain("operator_shifts");
        sql.Should().NotContain("hr_identity_mappings");
        sql.Should().NotContain("active_shift");
    }

    [Fact]
    public void ProvisioningScript_TakesBackupBeforeApplyingDatabaseMutation()
    {
        var script = ReadRepoFile("scripts", "v1.3", "local-runtime", "Initialize-OperatorConsoleControlledWorkstation.ps1");

        script.IndexOf("pg_dump", StringComparison.Ordinal).Should().BeLessThan(
            script.LastIndexOf("Invoke-ProvisioningSql -ApplyChanges:$true", StringComparison.Ordinal));
        script.Should().Contain("Invoke-ProvisioningSql -ApplyChanges:$false");
        script.ToLowerInvariant().Should().NotContain("shift");
    }

    [Fact]
    public void OperatingContextMigration_MakesShiftNullableAndPreservesHistoricalForeignKey()
    {
        var migration = ReadRepoFile("infra", "db", "patches", "ExitPass_OperatorConsoleDeferShiftControl_v1.3.sql");
        var validator = ReadRepoFile("infra", "db", "patches", "validation", "Validate_OperatorConsoleDeferShiftControl_v1.3.sql");

        migration.Should().Contain("ALTER COLUMN operator_shift_id DROP NOT NULL");
        migration.Should().NotContain("DROP CONSTRAINT");
        migration.Should().NotContain("DELETE FROM");
        validator.Should().Contain("fk_operator_session_contexts__operator_shift");
        validator.Should().Contain("attnotnull");
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "scripts")))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull("the test must run from inside the ExitPass repository");
        return File.ReadAllText(Path.Combine([current!.FullName, .. parts]));
    }
}
