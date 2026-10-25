using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperationsSupervisorRbacReconciliationTests
{
    [Fact]
    public void Preflight_IsReadOnlyExactIdentityAndPITXScoped()
    {
        var source = ReadRepositoryFile(
            "scripts", "v1.3", "local-runtime", "sql",
            "Inspect-PersistentIstOperationsSupervisorRbacReconciliation.sql");

        source.Should().Contain("BEGIN TRANSACTION READ ONLY;");
        source.Should().Contain("PERSISTENT_IST_DATABASE_REQUIRED");
        source.Should().Contain("d94bf15c-75b8-4869-bb79-0f2ac02aa841");
        source.Should().Contain("JuanDC05");
        source.Should().Contain("77000000-0000-0000-0000-000000000012");
        source.Should().Contain("uat-operations-supervisor");
        source.Should().Contain("2d1dcdf8-f563-537c-8542-0bde7cc9da97");
        source.Should().Contain("PITX-LEVEL-3");
        source.Should().Contain("UNEXPECTED_ACTIVE_SCOPE");
        source.Should().Contain("INCOMPATIBLE_ACTIVE_OPERATIONS_SUPERVISOR_USER_TYPE");
        source.Should().Contain("canonical_permission_count");
        source.Should().Contain("ROLLBACK;");
    }

    [Fact]
    public void Patch_UpdatesCanonicalRoleWithoutCreatingUserSpecificRoleAssignments()
    {
        var source = ReadRepositoryFile(
            "infra", "db", "patches",
            "ExitPass_CentralPms_OperationsSupervisorRbacReconciliation_v1.3.sql");

        source.Should().Contain("pg_advisory_xact_lock");
        source.Should().Contain("OPERATIONS_SUPERVISOR_CANONICAL_PARITY");
        source.Should().Contain("operations_supervisor_expected_permissions");
        source.Should().Contain("INSERT INTO identity.role_permissions");
        source.Should().Contain("OPERATIONS_SUPERVISOR_PERMISSION_COUNT_NOT_30");
        source.Should().Contain("INCOMPATIBLE_ACTIVE_OPERATIONS_SUPERVISOR_USER_TYPE");
        source.Should().Contain("OPERATIONS_SUPERVISOR_SCOPE_MUST_REMAIN_SITE_ONLY");
        source.Should().Contain("authorization_epoch = user_record.authorization_epoch + 1");
        source.Should().Contain("session_status = 'REVOKED'");
        source.Should().Contain("CREDENTIAL_VERSION_CHANGED");
        source.Should().Contain("AUTHORIZATION_EPOCH_INVALID");
        source.Should().NotContain("INSERT INTO identity.user_roles");
        source.Should().NotContain("UPDATE identity.user_roles");
        source.Should().NotContain("UPDATE identity.local_credentials");
        source.Should().NotContain("UPDATE identity.user_mfa_authenticators");
        source.Should().NotContain("password_verifier");
        source.Should().NotContain("protected_secret_envelope");
    }

    [Fact]
    public void Validator_ProvesParityScopeSegregationAndCanonicalRoleCompleteness()
    {
        var source = ReadRepositoryFile(
            "infra", "db", "patches", "validation",
            "Validate_CentralPmsOperationsSupervisorRbacReconciliation_v1.3.sql");

        source.Should().Contain("EFFECTIVE_PERMISSION_PARITY_FAILED");
        source.Should().Contain("OPERATIONS_SUPERVISOR_PERMISSION_COUNT_NOT_30");
        source.Should().Contain("OPERATIONS_SUPERVISOR_PERMISSION_SET_INVALID");
        source.Should().Contain("OPERATIONS_SUPERVISOR_HAS_CASHIER_OR_INITIATOR_AUTHORITY");
        source.Should().Contain("OPERATIONS_SUPERVISOR_HAS_ADMINISTRATIVE_PRIVILEGE");
        source.Should().Contain("INCOMPATIBLE_ACTIVE_OPERATIONS_SUPERVISOR_USER_TYPE");
        source.Should().Contain("OPERATIONS_SUPERVISOR_SCOPE_NOT_SITE_ONLY");
        source.Should().Contain("fiscal-reporting.z.generate");
        source.Should().Contain("apt.access");
        source.Should().Contain("cash-custody-movements.acknowledge");
        source.Should().Contain("cash-custody-movements.correct");
        source.Should().Contain("cash-custody-movements.read");
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
