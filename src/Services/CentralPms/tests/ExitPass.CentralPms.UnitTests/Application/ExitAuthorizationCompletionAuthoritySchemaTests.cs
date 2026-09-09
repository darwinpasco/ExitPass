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

    [Fact]
    public void IssuanceRoutine_UsesOneCanonicalRoutineForPaidAndStatutoryCompletion()
    {
        var source = ReadIssuancePatch();

        source.Should().Contain("p_completion_basis varchar DEFAULT 'PAYMENT_FINALITY'");
        source.Should().Contain("IF p_completion_basis = 'PAYMENT_FINALITY' THEN");
        source.Should().Contain("ELSIF p_completion_basis <> 'ZERO_PAYABLE_STATUTORY_FINALITY' THEN");
        source.Should().Contain("payment attempt % has no reconciled recorded payment confirmation");
        source.Should().Contain("zero-payable applied tariff has conflicting payment ancestry");
    }

    [Fact]
    public void IssuanceRoutine_RederivesZeroPayableStatutoryAndFiscalAncestry()
    {
        var source = ReadIssuancePatch();

        source.Should().Contain("decision.decision_result_status = 'APPROVED'");
        source.Should().Contain("v_application_command.command_status = 'APPLIED'");
        source.Should().Contain("validation.validation_status = 'APPROVED'");
        source.Should().Contain("ROUND(tariff.net_amount * 100)::bigint = 0");
        source.Should().Contain("fiscal.completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'");
        source.Should().Contain("fiscal.pos_server_fiscal_document_id IS NOT NULL");
        source.Should().Contain("fiscal.electronic_journal_event_reference IS NOT NULL");
        source.Should().Contain("v_authorization.payment_attempt_id IS NOT NULL");
        source.Should().Contain("parking session already has an incompatible ExitAuthorization");
    }

    private static string ReadMigration() => File.ReadAllText(FindRepositoryFile(
        "infra",
        "db",
        "patches",
        "ExitPass_ExitAuthorizationCompletionAuthority_v1.3.sql"));

    private static string ReadIssuancePatch() => File.ReadAllText(FindRepositoryFile(
        "infra",
        "db",
        "patches",
        "ExitPass_Core_IssueExitAuthorization_v1.3.sql"));

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
