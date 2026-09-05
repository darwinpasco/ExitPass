using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ShiftManagementSchemaTests
{
    [Fact]
    public void Patch_KeepsOneShiftAuthorityAndRemovesScheduleAsOperationalPrerequisite()
    {
        var source = File.ReadAllText(FindRepositoryFile("infra", "db", "patches", "ExitPass_ShiftManagementMvp_v1.3.sql"));
        source.Should().Contain("ALTER COLUMN scheduled_start_at DROP NOT NULL");
        source.Should().Contain("ALTER COLUMN hr_identity_mapping_id DROP NOT NULL");
        source.Should().Contain("ux_operator_shifts__one_active_per_user");
        source.Should().Contain("WHERE operational_status = 'ACTIVE' AND revoked_at IS NULL");
        source.Should().NotContain("CREATE TABLE IF NOT EXISTS operator_console.operational_shifts");
    }

    [Fact]
    public void Repository_UsesCurrentRoleScopeAndFailsClosedOnDeviceMismatchAndOpenCustody()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Infrastructure", "ShiftManagement", "PostgresShiftManagementRepository.cs"));
        source.Should().Contain("identity.user_role_scope_grants");
        source.Should().Contain("sites.device_assignments");
        source.Should().Contain("operator_device_assignment_history");
        source.Should().Contain("cash_custody_status <> 'OPEN'");
        source.Should().Contain("operations.operator_action_logs");
    }

    [Fact]
    public void LoginEndpoint_DoesNotRejectValidAuthenticationForMissingOperationalContext()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Api", "Endpoints", "HumanAuthenticationEndpoints.cs"));
        source.Should().Contain("if (!operating.Succeeded || operating.Context is null) return result;");
        source.Should().NotContain("return SafeFailure(StatusCodes.Status403Forbidden, binding.ErrorCode");
    }

    [Fact]
    public void PersistentImporter_AddsOnlyIdempotentShiftManagementSupervisorBindings()
    {
        var source = File.ReadAllText(FindRepositoryFile("scripts", "v1.3", "catalog", "Update-PersistentRealSiteOperationalConfiguration.sql"));
        source.Should().Contain("shift-management.view");
        source.Should().Contain("shift-management.manage");
        source.Should().Contain("OPERATIONS_SUPERVISOR");
        source.Should().Contain("ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE");
        source.Should().Contain("ON CONFLICT (role_permission_id) DO UPDATE");
        source.Should().NotContain("SITE_OPERATOR', 'shift-management.manage");
    }

    [Fact]
    public void ShiftManagementMutations_UseHumanSessionAntiforgeryValidation()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Api", "Endpoints", "ShiftManagementEndpoints.cs"));
        source.Should().Contain("ValidateCsrfAsync");
        source.Should().Contain("antiforgery.ValidateRequestAsync");
        source.Should().Contain("CSRF_VALIDATION_FAILED");
    }

    [Fact]
    public void ShiftManagementReads_DelegateOwnerOrSupervisorAuthorizationToService()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "Services", "CentralPms", "src", "ExitPass.CentralPms.Api", "Endpoints", "ShiftManagementEndpoints.cs"));

        source.Should().Contain("var management = app.MapGroup(\"/v1/operator-console/shift-management\")");
        source.Should().Contain(".RequireAuthorization()");
        source.Should().Contain("management.MapGet(\"/shifts\", ListAsync);");
        source.Should().Contain("management.MapGet(\"/shifts/{shiftId:guid}\", GetAsync);");
        source.Should().Contain("ReconciliationPolicyMetadata(\"ShiftManagementManage\")");
        source.Should().NotContain("ReconciliationPolicyMetadata(\"ShiftManagementView\")");
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ExitPass.sln"))) directory = directory.Parent;
        directory.Should().NotBeNull("the test must run below the repository root");
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
