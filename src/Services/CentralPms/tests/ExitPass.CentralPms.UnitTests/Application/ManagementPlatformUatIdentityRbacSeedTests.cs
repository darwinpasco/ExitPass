using System.Text.RegularExpressions;
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
        "OPERATOR_SUPPORT_STAFF",
        "FINANCE_RECONCILIATION_ANALYST",
        "COMPLIANCE_POLICY_ADMINISTRATOR",
        "EXECUTIVE_MANAGEMENT"
    ];

    [Fact]
    public void SeedSql_DefinesSevenUatUsersAndRoleBundles()
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
    public void SeedSql_AssignsGranularPermissionsWithoutMicroRoles()
    {
        var sql = ReadRepoFile("scripts", "management-platform", "Seed-ManagementPlatformUatIdentityRbac.sql");
        var rolePermissions = ExtractRolePermissions(sql);

        rolePermissions.Should().HaveCount(7);

        rolePermissions["SYSTEM_RBAC_ADMINISTRATOR"].Should().Contain("management-platform.identity-rbac.inventory.read");
        rolePermissions["SYSTEM_RBAC_ADMINISTRATOR"].Should().Contain("rbac.manage");
        rolePermissions["SYSTEM_RBAC_ADMINISTRATOR"].Should().NotContain(
            [
                "statutory-discounts.decision.approve",
                "statutory-discounts.payable-basis.apply",
                "fiscal-issuance.void.command",
                "reconciliation.manage"
            ]);

        rolePermissions["OPERATOR_SUPPORT_STAFF"].Should().Contain(
            [
                "statutory-discounts.session.lookup",
                "statutory-discounts.draft.create",
                "statutory-discounts.evidence.capture",
                "statutory-discounts.policy.resolve"
            ]);
        rolePermissions["OPERATOR_SUPPORT_STAFF"].Should().NotContain(
            [
                "statutory-discounts.decision.approve",
                "statutory-discounts.decision.reject",
                "statutory-discounts.payable-basis.apply"
            ]);

        rolePermissions["OPERATIONS_SUPERVISOR"].Should().Contain(
            [
                "statutory-discounts.decision.approve",
                "statutory-discounts.decision.reject",
                "statutory-discounts.payable-basis.apply",
                "statutory-discounts.policy.resolve",
                "fiscal-issuance.void.command"
            ]);
        rolePermissions["OPERATIONS_SUPERVISOR"].Should().NotContain(["user.manage", "rbac.manage"]);

        rolePermissions["COMPLIANCE_POLICY_ADMINISTRATOR"].Should().Contain(
            [
                "statutory-discounts.audit.read",
                "policy-import.approve",
                "operator-console.policy-import-review.review"
            ]);
        rolePermissions["COMPLIANCE_POLICY_ADMINISTRATOR"].Should().NotContain(
            [
                "statutory-discounts.decision.approve",
                "statutory-discounts.payable-basis.apply",
                "fiscal-issuance.void.command"
            ]);

        rolePermissions["FINANCE_RECONCILIATION_ANALYST"].Should().Contain(["reconciliation.view", "reports.view"]);
        rolePermissions["FINANCE_RECONCILIATION_ANALYST"].Should().NotContain("statutory-discounts.decision.approve");

        rolePermissions["EXECUTIVE_MANAGEMENT"].Should().Contain(
            [
                "dashboard.view",
                "reports.view",
                "executive-summary.view"
            ]);
        var executiveMutationPermissions = rolePermissions["EXECUTIVE_MANAGEMENT"].Where(permission =>
            permission.EndsWith(".manage", StringComparison.Ordinal) ||
            permission.EndsWith(".command", StringComparison.Ordinal) ||
            permission.EndsWith(".apply", StringComparison.Ordinal) ||
            permission.EndsWith(".approve", StringComparison.Ordinal) ||
            permission.EndsWith(".reject", StringComparison.Ordinal) ||
            permission == "statutory-discounts.draft.create" ||
            permission == "statutory-discounts.evidence.capture" ||
            permission == "reports.export");
        executiveMutationPermissions.Should().BeEmpty();
    }

    [Fact]
    public void VerifySql_AssertsLeastPrivilegeAndFixtureCompatibility()
    {
        var sql = ReadRepoFile("scripts", "management-platform", "Verify-ManagementPlatformUatIdentityRbac.sql");

        sql.Should().Contain("centralpms_operator_uat_aligned_local");
        sql.Should().Contain("centralpms_aligned_discount_payment_si_runtime_local");
        sql.Should().Contain("centralpms_aligned_discount_exit_authorization_runtime_local");
        sql.Should().Contain("system_rbac_admin_lacks_business_mutation");
        sql.Should().Contain("executive_management_is_read_only");
        sql.Should().Contain("operator_support_requester_only");
        sql.Should().Contain("operations_supervisor_approves_without_rbac_admin");
        sql.Should().Contain("policy_import_runtime_boundary_preserved");
        sql.Should().Contain("uat_device_binding_count");
        sql.Should().Contain("uat_active_shift_count");
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
        script.Should().Contain("statutory-discounts.payable-basis.apply");
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
        script.Should().Contain("Reviewer/apply actor");
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

    private static Dictionary<string, HashSet<string>> ExtractRolePermissions(string sql)
    {
        var roleCodes = RoleCodes.ToHashSet(StringComparer.Ordinal);
        var map = RoleCodes.ToDictionary(role => role, _ => new HashSet<string>(StringComparer.Ordinal));

        foreach (Match match in Regex.Matches(
            sql,
            @"\('(?<role>[A-Z_]+)',\s*'(?<permission>[^']+)'\)",
            RegexOptions.CultureInvariant))
        {
            var role = match.Groups["role"].Value;
            if (!roleCodes.Contains(role))
            {
                continue;
            }

            map[role].Add(match.Groups["permission"].Value);
        }

        return map;
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
