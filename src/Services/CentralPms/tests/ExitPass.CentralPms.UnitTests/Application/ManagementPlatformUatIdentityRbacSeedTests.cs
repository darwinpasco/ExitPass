using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformUatIdentityRbacSeedTests
{
    private static readonly string[] RoleCodes =
    [
        "SYSTEM_RBAC_ADMINISTRATOR",
        "PLATFORM_ADMINISTRATOR",
        "OPERATIONS_SUPERVISOR",
        "SITE_OPERATOR",
        "FINANCE_RECONCILIATION_ANALYST",
        "COMPLIANCE_POLICY_ADMINISTRATOR",
        "EXECUTIVE_MANAGEMENT"
    ];

    [Fact]
    public void SeedSql_AssignsSevenUatUsersOnlyToCompatibleCanonicalRoles()
    {
        var sql = ReadRepoFile("scripts", "management-platform", "Seed-ManagementPlatformUatIdentityRbac.sql");

        sql.Should().Contain("uat-system-rbac-admin");
        sql.Should().Contain("uat-platform-admin");
        sql.Should().Contain("uat-operations-supervisor");
        sql.Should().Contain("uat-operator-support");
        sql.Should().Contain("uat-finance-reconciliation");
        sql.Should().Contain("uat-compliance-policy-admin");
        sql.Should().Contain("uat-executive-management");

        foreach (var roleCode in RoleCodes)
        {
            sql.Should().Contain(roleCode);
        }

        sql.Should().Contain("'INTERNAL_ADMIN', 'PLATFORM_ADMINISTRATOR'");
        sql.Should().Contain("'OPERATIONS_USER', 'OPERATIONS_SUPERVISOR'");
        sql.Should().Contain("'SITE_OPERATOR', 'SITE_OPERATOR'");
        sql.Should().Contain("'OTHER', 'EXECUTIVE_MANAGEMENT'");
        sql.Should().Contain("role.role_provenance = 'CANONICAL_ROLE'");
        sql.Should().Contain("identity.role_user_type_compatibility");
    }

    [Fact]
    public void SeedSql_PreservesStatutoryDiscountTwoUserFixtureIds()
    {
        var sql = ReadRepoFile("scripts", "management-platform", "Seed-ManagementPlatformUatIdentityRbac.sql");

        sql.Should().Contain("77000000-0000-0000-0000-000000000010");
        sql.Should().Contain("77000000-0000-0000-0000-000000000012");
        sql.Should().Contain("('77000000-0000-0000-0000-000000000010', 'uat-operator-support'");
        sql.Should().Contain("('77000000-0000-0000-0000-000000000012', 'uat-operations-supervisor'");
    }

    [Fact]
    public void SeedSql_DoesNotCreateOrRewriteRuntimeRolesOrRolePermissions()
    {
        var sql = ReadRepoFile("scripts", "management-platform", "Seed-ManagementPlatformUatIdentityRbac.sql");
        sql.Should().NotContain("INSERT INTO identity.roles (");
        sql.Should().NotContain("INSERT INTO identity.role_permissions (");
        sql.Should().NotContain("UPDATE identity.role_permissions");
        sql.Should().Contain("does not insert roles or role-permission bindings");
        sql.Should().Contain("WAVE3_UAT_ROLE_REPLACED");
    }

    [Fact]
    public void VerifySql_AssertsLeastPrivilegeAndFixtureCompatibility()
    {
        var sql = ReadRepoFile("scripts", "management-platform", "Verify-ManagementPlatformUatIdentityRbac.sql");

        sql.Should().Contain("centralpms_operator_uat_aligned_local");
        sql.Should().Contain("centralpms_aligned_discount_payment_si_runtime_local");
        sql.Should().Contain("centralpms_aligned_discount_exit_authorization_runtime_local");
        sql.Should().Contain("role_provenance='CANONICAL_ROLE'");
        sql.Should().Contain("identity.role_user_type_compatibility");
        sql.Should().Contain("uat-fixture.manage is assigned to a canonical role");
        sql.Should().Contain("superseded UAT bundles");
    }

    [Fact]
    public void StatutoryDiscountPreflight_ComposesManagementPlatformSeedAndUsesRequesterProfile()
    {
        var script = ReadRepoFile("scripts", "operator-console", "Invoke-StatutoryDiscountPilotPreflight.ps1");

        script.Should().Contain("Seed-ManagementPlatformUatIdentityRbac.sql");
        script.Should().Contain("Verify-ManagementPlatformUatIdentityRbac.sql");
        script.Should().Contain("statutory-discounts.session.lookup");
        script.Should().Contain("statutory-discounts.draft.create");
        script.Should().Contain("statutory-discounts.evidence.capture");
        script.Should().Contain("statutory-discounts.decision.approve");
        script.Should().NotContain("statutory-discounts.payable-basis.apply");
        script.Should().NotContain("operator-console.policy-import-review.submit,operator-console.policy-import-review.view-own,operator-console.policy-import-review.review,fiscal-issuance.status.read");
    }

    [Fact]
    public void AlignedDbPreflight_UsesCanonicalDbOutputAndTwoUserUatProfile()
    {
        var script = ReadRepoFile("scripts", "operator-console", "Invoke-StatutoryDiscountOperatorUatAlignedDbPreflight.ps1");

        script.Should().Contain("exitpassdb_v1.2");
        script.Should().Contain("exitpass-full-object.generated.sql");
        script.Should().Contain("Validate-V13CentralPmsAlignment.sql");
        script.Should().Contain("centralpms_operator_uat_aligned_local");
        script.Should().Contain("Seed-ManagementPlatformUatIdentityRbac.sql");
        script.Should().Contain("Seed-StatutoryDiscountPilotFixture.sql");
        script.Should().Contain("uat-operator-support");
        script.Should().Contain("uat-operations-supervisor");
        script.Should().Contain("Requester/evidence actor");
        script.Should().Contain("Reviewer actor");
        script.Should().NotContain("Reviewer/apply actor");
        script.Should().Contain("gross=12500 vatExclusive=11161 vat=1339 discount=2232 final=8929");
    }

    [Fact]
    public void AlignedDbPaymentSalesInvoiceRuntimeProof_UsesCanonicalDbOutputAndLivePosProof()
    {
        var script = ReadRepoFile("scripts", "central-pms", "Invoke-AlignedDbStatutoryDiscountPaymentSalesInvoiceRuntimeProof.ps1");

        script.Should().Contain("exitpass-full-object.generated.sql");
        script.Should().Contain("Validate-V13CentralPmsAlignment.sql");
        script.Should().Contain("centralpms_aligned_discount_payment_si_runtime_local");
        script.Should().Contain("Seed-ManagementPlatformUatIdentityRbac.sql");
        script.Should().Contain("Seed-StatutoryDiscountPilotFixture.sql");
        script.Should().Contain("EXITPASS_RUN_STATUTORY_DISCOUNT_LIVE_POS_SMOKE");
        script.Should().Contain("LocalRuntime_WhenEnabled_IssuesDiscountedSalesInvoiceThroughCentralPmsLivePosServer");
        script.Should().Contain("http://localhost:5000");
    }

    [Fact]
    public void AlignedDbDiscountedPaymentExitAuthorizationReadinessProof_UsesCanonicalDbOutputAndLivePosProof()
    {
        var script = ReadRepoFile("scripts", "central-pms", "Invoke-AlignedDbDiscountedPaymentExitAuthorizationReadinessProof.ps1");

        script.Should().Contain("exitpass-full-object.generated.sql");
        script.Should().Contain("Validate-V13CentralPmsAlignment.sql");
        script.Should().Contain("centralpms_aligned_discount_exit_authorization_runtime_local");
        script.Should().Contain("Seed-ManagementPlatformUatIdentityRbac.sql");
        script.Should().Contain("Seed-StatutoryDiscountPilotFixture.sql");
        script.Should().Contain("EXITPASS_RUN_STATUTORY_DISCOUNT_LIVE_POS_SMOKE");
        script.Should().Contain("LocalRuntime_WhenEnabled_DiscountedPaymentAndFiscalIssuanceAreReadyForExitAuthorization");
        script.Should().Contain("http://localhost:5000");
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
