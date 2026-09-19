using ExitPass.CentralPms.Application.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class PersistentIstUatOperationsSupervisorRoleReconciliationTests
{
    [Fact]
    public void SourceCatalogAndUatSeed_KeepTheCanonicalOperationsSupervisorContract()
    {
        ApprovedIdentityRoleCatalog.AssignableCodes.Should().HaveCount(8);
        ApprovedIdentityRoleCatalog.AssignableCodes.Should().Contain(ApprovedIdentityRoleCatalog.OperationsSupervisor);
        ApprovedIdentityRoleCatalog.AssignableCodes.Should().NotContain("UAT_OPERATIONS_SUPERVISOR");
        ApprovedIdentityRoleCatalog.IsApplicationEligible(
            ApprovedIdentityRoleCatalog.OperationsSupervisor,
            ApprovedIdentityRoleCatalog.OperatorConsoleAudience).Should().BeTrue();
        ApprovedIdentityRoleCatalog.IsApplicationEligible(
            "UAT_OPERATIONS_SUPERVISOR",
            ApprovedIdentityRoleCatalog.OperatorConsoleAudience).Should().BeFalse();

        var seed = ReadRepoFile("scripts", "management-platform", "Seed-ManagementPlatformUatIdentityRbac.sql");
        seed.Should().Contain("('77000000-0000-0000-0000-000000000012', 'uat-operations-supervisor', 'uat-operations-supervisor@example.test', 'UAT Operations Supervisor', 'OPERATIONS_USER', 'OPERATIONS_SUPERVISOR')");
        seed.Should().NotContain("UAT_OPERATIONS_SUPERVISOR");
        seed.Should().NotContain("'exitpass_ist'");
    }

    [Fact]
    public void RepairTool_IsExactTargetTransactionalAndFailClosed()
    {
        var wrapper = ReadRepoFile("scripts", "v1.3", "local-runtime", "Repair-PersistentIstUatOperationsSupervisorRole.ps1");
        var inspect = ReadRepoFile("scripts", "v1.3", "local-runtime", "sql", "Inspect-PersistentIstUatOperationsSupervisorRole.sql");
        var repair = ReadRepoFile("scripts", "v1.3", "local-runtime", "sql", "Repair-PersistentIstUatOperationsSupervisorRole.sql");

        wrapper.Should().Contain("[switch]$PreflightOnly");
        wrapper.Should().Contain("[switch]$Apply");
        wrapper.Should().Contain("--single-transaction");
        wrapper.Should().Contain("pg_dump");
        wrapper.Should().Contain("exitpass-ist-pre-uat-operations-supervisor-role-reconciliation");

        inspect.Should().Contain("BEGIN TRANSACTION READ ONLY;");
        inspect.Should().Contain("PERSISTENT_IST_DATABASE_REQUIRED");
        inspect.Should().Contain("TARGET_IDENTITY_MISMATCH");
        inspect.Should().Contain("CANONICAL_ROLE_UNAVAILABLE_OR_INCOMPATIBLE");
        inspect.Should().Contain("AMBIGUOUS_CANONICAL_ASSIGNMENT");
        inspect.Should().Contain("PITX_SITE_SCOPE_EVIDENCE_AMBIGUOUS");

        repair.Should().Contain("current_database() <> 'exitpass_ist'");
        repair.Should().Contain("77000000-0000-0000-0000-000000000012");
        repair.Should().Contain("uat-operations-supervisor");
        repair.Should().Contain("PERSISTENT_IST_CANONICAL_ROLE_RECONCILIATION");
        repair.Should().Contain("pg_advisory_xact_lock");
        repair.Should().Contain("UNEXPECTED_ACTIVE_ROLE");
        repair.Should().Contain("TARGET_USER_TYPE_UNEXPECTED");
        repair.Should().Contain("UNEXPECTED_SCOPE_EVIDENCE");
        repair.Should().Contain("DETERMINISTIC_ASSIGNMENT_ID_COLLISION");
        repair.Should().Contain("DETERMINISTIC_SCOPE_ID_COLLISION");
    }

    [Fact]
    public void RepairTool_PreservesOnlyValidatedSiteScopeAndInvalidatesStaleSessionsOnce()
    {
        var repair = ReadRepoFile("scripts", "v1.3", "local-runtime", "sql", "Repair-PersistentIstUatOperationsSupervisorRole.sql");

        repair.Should().Contain("2d1dcdf8-f563-537c-8542-0bde7cc9da97");
        repair.Should().Contain("a6dbadf6-68b5-5bed-a7e0-a75faee70841");
        repair.Should().Contain("scope_type, site_id");
        repair.Should().Contain("'SITE', c_site_id");
        repair.Should().NotContain("'SITE_GROUP', c_site_group_id");
        repair.Should().Contain("v_authority_changed :=");
        repair.Should().Contain("IF NOT v_authority_changed THEN");
        repair.Should().Contain("authorization_epoch = authorization_epoch + 1");
        repair.Should().Contain("session_status = 'REVOKED'");
        repair.Should().Contain("authorization_epoch FROM identity.users");
        repair.Should().Contain("expected exactly one increment");
    }

    [Fact]
    public void RepairTool_DoesNotTouchCredentialsMfaOrUnrelatedUsers()
    {
        var repair = ReadRepoFile("scripts", "v1.3", "local-runtime", "sql", "Repair-PersistentIstUatOperationsSupervisorRole.sql");

        repair.Contains("UPDATE identity.local_credentials", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        repair.Contains("UPDATE identity.user_mfa_authenticators", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        repair.Contains("password_verifier", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        repair.Contains("protected_secret_envelope", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        repair.Contains("credential_version = credential_version + 1", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        repair.Should().Contain("WHERE user_id = c_user_id");
        repair.Should().Contain("credential_version FROM identity.users");
        repair.Should().Contain("CREDENTIAL_VERSION_CHANGED");
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
