using ExitPass.CentralPms.Application.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ApprovedIdentityRoleCatalogTests
{
    [Fact]
    public void Catalog_ContainsExactlyTheEightApprovedAssignableRoles()
    {
        ApprovedIdentityRoleCatalog.AssignableCodes.Should().BeEquivalentTo([
            "SYSTEM_ADMINISTRATOR", "OPERATIONS_SUPERVISOR", "SITE_OPERATOR", "PARKING_ATTENDANT",
            "APT_CASHIER_OPERATOR", "FINANCE_RECONCILIATION_ANALYST",
            "COMPLIANCE_POLICY_ADMINISTRATOR", "EXECUTIVE_MANAGEMENT"
        ]);
        ApprovedIdentityRoleCatalog.IsAssignable("PLATFORM_ADMINISTRATOR").Should().BeFalse();
        ApprovedIdentityRoleCatalog.IsAssignable("SYSTEM_RBAC_ADMINISTRATOR").Should().BeFalse();
        ApprovedIdentityRoleCatalog.IsAssignable("HEAD_OFFICE_STATUTORY_BENEFIT_REVIEWER").Should().BeFalse();
    }

    [Theory]
    [InlineData("SYSTEM_ADMINISTRATOR", "MANAGEMENT_PLATFORM", true)]
    [InlineData("SYSTEM_ADMINISTRATOR", "OPERATOR_CONSOLE", false)]
    [InlineData("SYSTEM_ADMINISTRATOR", "APT", false)]
    [InlineData("SYSTEM_ADMINISTRATOR", "NATIVE_PARKING_APP", false)]
    [InlineData("OPERATIONS_SUPERVISOR", "MANAGEMENT_PLATFORM", true)]
    [InlineData("OPERATIONS_SUPERVISOR", "OPERATOR_CONSOLE", true)]
    [InlineData("SITE_OPERATOR", "MANAGEMENT_PLATFORM", false)]
    [InlineData("SITE_OPERATOR", "OPERATOR_CONSOLE", true)]
    [InlineData("SITE_OPERATOR", "APT", false)]
    [InlineData("SITE_OPERATOR", "NATIVE_PARKING_APP", false)]
    [InlineData("PARKING_ATTENDANT", "NATIVE_PARKING_APP", true)]
    [InlineData("PARKING_ATTENDANT", "APT", false)]
    [InlineData("PARKING_ATTENDANT", "OPERATOR_CONSOLE", false)]
    [InlineData("PARKING_ATTENDANT", "MANAGEMENT_PLATFORM", false)]
    [InlineData("APT_CASHIER_OPERATOR", "APT", true)]
    [InlineData("APT_CASHIER_OPERATOR", "MANAGEMENT_PLATFORM", false)]
    [InlineData("APT_CASHIER_OPERATOR", "OPERATOR_CONSOLE", false)]
    [InlineData("APT_CASHIER_OPERATOR", "NATIVE_PARKING_APP", false)]
    [InlineData("FINANCE_RECONCILIATION_ANALYST", "MANAGEMENT_PLATFORM", true)]
    [InlineData("FINANCE_RECONCILIATION_ANALYST", "OPERATOR_CONSOLE", false)]
    [InlineData("COMPLIANCE_POLICY_ADMINISTRATOR", "MANAGEMENT_PLATFORM", true)]
    [InlineData("COMPLIANCE_POLICY_ADMINISTRATOR", "OPERATOR_CONSOLE", false)]
    [InlineData("EXECUTIVE_MANAGEMENT", "MANAGEMENT_PLATFORM", true)]
    [InlineData("EXECUTIVE_MANAGEMENT", "OPERATOR_CONSOLE", false)]
    public void ApplicationEligibility_IsRoleSpecific(string role, string audience, bool expected) =>
        ApprovedIdentityRoleCatalog.IsApplicationEligible(role, audience).Should().Be(expected);

    [Theory]
    [InlineData("EXECUTIVE_MANAGEMENT", "GLOBAL", true)]
    [InlineData("EXECUTIVE_MANAGEMENT", "SITE", false)]
    [InlineData("EXECUTIVE_MANAGEMENT", "SITE_GROUP", false)]
    [InlineData("SYSTEM_ADMINISTRATOR", "GLOBAL", true)]
    [InlineData("SYSTEM_ADMINISTRATOR", "SITE", false)]
    [InlineData("SYSTEM_ADMINISTRATOR", "SITE_GROUP", false)]
    [InlineData("OPERATIONS_SUPERVISOR", "SITE", true)]
    [InlineData("OPERATIONS_SUPERVISOR", "SITE_GROUP", false)]
    [InlineData("OPERATIONS_SUPERVISOR", "GLOBAL", false)]
    [InlineData("SITE_OPERATOR", "SITE_GROUP", false)]
    [InlineData("APT_CASHIER_OPERATOR", "GLOBAL", false)]
    [InlineData("FINANCE_RECONCILIATION_ANALYST", "GLOBAL", true)]
    [InlineData("PLATFORM_ADMINISTRATOR", "SITE", false)]
    public void ScopeEligibility_FailsClosed(string role, string scope, bool expected) =>
        ApprovedIdentityRoleCatalog.IsScopeAllowed(role, scope).Should().Be(expected);

    [Fact]
    public void UserEligibility_AllowsAnAudienceWhenAnyApprovedAssignedRoleAllowsIt()
    {
        ApprovedIdentityRoleCatalog.IsUserEligibleForApplication(
            [ApprovedIdentityRoleCatalog.SiteOperator, ApprovedIdentityRoleCatalog.FinanceReconciliationAnalyst],
            ApprovedIdentityRoleCatalog.ManagementPlatformAudience).Should().BeTrue();

        ApprovedIdentityRoleCatalog.IsUserEligibleForApplication(
            [ApprovedIdentityRoleCatalog.SiteOperator],
            ApprovedIdentityRoleCatalog.AptAudience).Should().BeFalse();
        ApprovedIdentityRoleCatalog.IsUserEligibleForApplication(null, "MANAGEMENT_PLATFORM").Should().BeFalse();
    }

    [Theory]
    [InlineData("statutory-discounts.review.queue.read")]
    [InlineData("statutory-discounts.review.detail.read")]
    [InlineData("statutory-discounts.evidence.review.view")]
    [InlineData("statutory-discounts.decision.review")]
    [InlineData("statutory-discounts.decision.approve")]
    [InlineData("statutory-discounts.decision.reject")]
    [InlineData("statutory-discounts.payable-basis.apply")]
    public void StatutorySupervisorPermissions_AreManagementPlatformOnly(string permission)
    {
        ApprovedIdentityRoleCatalog.IsPermissionEligibleForApplication(permission, "MANAGEMENT_PLATFORM").Should().BeTrue();
        ApprovedIdentityRoleCatalog.IsPermissionEligibleForApplication(permission, "OPERATOR_CONSOLE").Should().BeFalse();
        ApprovedIdentityRoleCatalog.IsPermissionEligibleForApplication(permission, "APT").Should().BeFalse();
        ApprovedIdentityRoleCatalog.IsPermissionEligibleForApplication(permission, "NATIVE_PARKING_APP").Should().BeFalse();
    }

    [Fact]
    public void Policies_ExposeTheCanonicalMetadataForH2()
    {
        ApprovedIdentityRoleCatalog.Policies.Should().HaveCount(8);
        ApprovedIdentityRoleCatalog.TryGetPolicy("EXECUTIVE_MANAGEMENT", out var executive).Should().BeTrue();
        executive!.DefaultAssignmentScope.Should().Be("GLOBAL");
        executive.AllowedAssignmentScopes.Should().Equal("GLOBAL");
        ApprovedIdentityRoleCatalog.TryGetPolicy("NOT_A_ROLE", out _).Should().BeFalse();
    }
}
