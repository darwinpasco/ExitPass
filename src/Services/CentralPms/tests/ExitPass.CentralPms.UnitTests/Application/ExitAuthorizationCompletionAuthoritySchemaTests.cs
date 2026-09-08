using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ExitAuthorizationCompletionAuthoritySchemaTests
{
    [Fact]
    public void Migration_BackfillsHistoricalPaymentAncestryAndPreservesPaymentRoutineCompatibility()
    {
        var source = ReadMigration();

        source.Should().Contain("SET tariff_snapshot_id = attempt.tariff_snapshot_id");
        source.Should().Contain("completion_basis = 'PAYMENT_FINALITY'");
        source.Should().Contain("ALTER COLUMN payment_attempt_id DROP NOT NULL");
        source.Should().Contain("ALTER COLUMN payment_confirmation_id DROP NOT NULL");
        source.Should().Contain("NEW.completion_basis IS NULL");
        source.Should().Contain("NEW.completion_basis := 'PAYMENT_FINALITY'");
    }

    [Fact]
    public void Migration_RequiresExactlyOneSupportedCompletionAncestryShape()
    {
        var source = ReadMigration();

        source.Should().Contain("ck_exit_authorizations__completion_ancestry");
        source.Should().Contain("completion_basis = 'PAYMENT_FINALITY'");
        source.Should().Contain("completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'");
        source.Should().Contain("payment_attempt_id IS NULL");
        source.Should().Contain("payment_confirmation_id IS NULL");
        source.Should().Contain("statutory_discount_decision_command_id IS NOT NULL");
        source.Should().Contain("statutory_discount_payable_basis_application_command_id IS NOT NULL");
        source.Should().Contain("statutory_discount_validation_id IS NOT NULL");
        source.Should().Contain("applied_policy_reference_id IS NOT NULL");
    }

    [Fact]
    public void Migration_EnforcesReferentialAndReplayIntegrityForBothBases()
    {
        var source = ReadMigration();
        var baseline = File.ReadAllText(FindRepositoryFile(
            "ExitPass_Full_Database_Creation_DDL_v1.2.sql"));

        baseline.Should().Contain("fk_exit_authorizations__payment_attempt_id");
        baseline.Should().Contain("fk_exit_authorizations__payment_confirmation_id");
        source.Should().Contain("fk_exit_authorizations__tariff_snapshot_id");
        source.Should().Contain("fk_exit_authorizations__statutory_decision_command_id");
        source.Should().Contain("fk_exit_authorizations__statutory_application_command_id");
        source.Should().Contain("fk_exit_authorizations__statutory_validation_id");
        source.Should().Contain("fk_exit_authorizations__applied_policy_reference_id");
        source.Should().Contain("ux_exit_authorizations__statutory_application_command");
        baseline.Should().Contain("ux_exit_authorizations__active_by_session");
        source.Should().Contain("v_confirmation_attempt_id IS DISTINCT FROM NEW.payment_attempt_id");
    }

    private static string ReadMigration() => File.ReadAllText(FindRepositoryFile(
        "infra",
        "db",
        "patches",
        "ExitPass_ExitAuthorizationCompletionAuthority_v1.3.sql"));

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }
}
