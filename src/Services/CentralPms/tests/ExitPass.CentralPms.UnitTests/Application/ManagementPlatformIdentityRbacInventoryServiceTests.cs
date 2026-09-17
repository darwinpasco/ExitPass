using ExitPass.CentralPms.Application.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformIdentityRbacInventoryServiceTests
{
    [Fact]
    public async Task GetInventoryAsync_ReturnsExactlyEightApprovedRoleBundles()
    {
        var service = new ManagementPlatformIdentityRbacInventoryService(new FakeRepository());

        var inventory = await service.GetInventoryAsync(CancellationToken.None);

        inventory.RoleBundles.Select(role => role.DisplayName).Should().BeEquivalentTo(new[]
        {
            "System Administrator",
            "Operations Supervisor",
            "Site Operator",
            "Parking Attendant",
            "APT / Cashier Operator",
            "Finance / Reconciliation Analyst",
            "Compliance / Policy Administrator",
            "Executive / Management"
        });
    }

    [Fact]
    public async Task GetInventoryAsync_SurfacesImplementedPermissionMappings()
    {
        var service = new ManagementPlatformIdentityRbacInventoryService(new FakeRepository());

        var inventory = await service.GetInventoryAsync(CancellationToken.None);

        inventory.Permissions.Should().Contain(permission =>
            permission.PermissionKey == "management-platform.identity-rbac.inventory.read" &&
            permission.Status == "implemented" &&
            permission.MappedPolicies.Contains("ManagementPlatformIdentityRbacInventoryRead"));

        inventory.Permissions.Should().Contain(permission =>
            permission.PermissionKey == "statutory-discounts.payable-basis.apply" &&
            permission.Status == "target-only" &&
            permission.MappedPolicies.Count == 0);

        inventory.RoleBundles.Single(role => role.RoleKey == "operations-supervisor")
            .TypicalAccessRights.Should().NotContain("statutory-discounts.payable-basis.apply");
        inventory.RoleBundles.Single(role => role.RoleKey == "operations-supervisor")
            .TypicalAccessRights.Should().Contain([
                "statutory-discounts.draft.create",
                "shift.manage",
                "statutory-discounts.review.queue.read",
                "statutory-discounts.review.detail.read",
                "statutory-discounts.decision.approve",
                "statutory-discounts.decision.reject"
            ]);

        var administrator = inventory.RoleBundles.Single(role => role.RoleKey == "system-administrator");
        administrator.TypicalAccessRights.Should().Contain(["user.manage", "site.manage", "device.manage", "shift.manage", "platform-config.manage"]);
        administrator.TypicalAccessRights.Should().NotContain(permission =>
            permission.StartsWith("statutory-discounts.", StringComparison.Ordinal) ||
            permission.StartsWith("reconciliation.", StringComparison.Ordinal) ||
            permission.StartsWith("policy-import.", StringComparison.Ordinal) ||
            permission.StartsWith("fiscal-issuance.void", StringComparison.Ordinal) ||
            permission.StartsWith("apt.", StringComparison.Ordinal));

        inventory.PolicyMappings.Should().NotContain(mapping =>
            mapping.PolicyName == "FiscalIssuanceVoidCommand");
        inventory.Permissions.Should().Contain(permission =>
            permission.PermissionKey == "fiscal-issuance.void.command" &&
            permission.Status == "target-only" &&
            permission.MappedPolicies.Count == 0);
    }

    [Fact]
    public async Task GetInventoryAsync_IncludesPersistedSafeInventoryAndGaps()
    {
        var service = new ManagementPlatformIdentityRbacInventoryService(new FakeRepository());

        var inventory = await service.GetInventoryAsync(CancellationToken.None);

        inventory.Users.Should().ContainSingle(user =>
            user.UserId == FakeRepository.UserId &&
            user.Username == "uat.operator");
        inventory.UserRoleAssignments.Should().ContainSingle();
        inventory.UserSiteScopes.Should().ContainSingle(scope => scope.SiteName == "Not available");
        inventory.DeviceBindings.Should().ContainSingle(binding => binding.DeviceLabel == "UAT Operator Console Device");
        inventory.Shifts.Should().ContainSingle();
        inventory.Gaps.Should().Contain(gap => gap.GapKey == "management-platform-ui-missing");
        inventory.Gaps.Should().Contain(gap => gap.GapKey == "admin-mutation-apis-missing-by-design");
    }

    private sealed class FakeRepository : IManagementPlatformIdentityRbacInventoryRepository
    {
        public static readonly Guid UserId = Guid.Parse("77000000-0000-0000-0000-000000000010");
        private static readonly Guid RoleId = Guid.Parse("77000000-0000-0000-0000-000000000090");
        private static readonly Guid SiteGroupId = Guid.Parse("77000000-0000-0000-0000-000000000001");
        private static readonly Guid SiteId = Guid.Parse("77000000-0000-0000-0000-000000000002");
        private static readonly Guid DeviceBindingId = Guid.Parse("77000000-0000-0000-0000-000000000030");
        private static readonly Guid ShiftId = Guid.Parse("77000000-0000-0000-0000-000000000050");

        public Task<ManagementPlatformIdentityRbacPersistenceInventory> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ManagementPlatformIdentityRbacPersistenceInventory(
                [
                    new ManagementPlatformIdentityUser(
                        UserId,
                        "uat.operator",
                        "UAT Operator",
                        "uat.operator@example.test",
                        "ACTIVE",
                        "SITE_OPERATOR",
                        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                        null)
                ],
                [
                    new ManagementPlatformUserRoleAssignment(
                        UserId,
                        RoleId,
                        "OPERATOR_SUPPORT_STAFF",
                        "Operator / Support Staff",
                        "ACTIVE",
                        "ACTIVE",
                        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                        null)
                ],
                [
                    new ManagementPlatformUserSiteScope(
                        UserId,
                        SiteGroupId,
                        SiteId,
                        "Not available",
                        "Not available",
                        "operator_console.operator_shifts",
                        "ACTIVE")
                ],
                [
                    new ManagementPlatformDeviceBinding(
                        DeviceBindingId,
                        "UAT Operator Console Device",
                        UserId,
                        SiteGroupId,
                        SiteId,
                        "ACTIVE",
                        "BROWSER_KEY_AND_MTLS",
                        DateTimeOffset.Parse("2026-07-01T00:00:00Z"))
                ],
                [
                    new ManagementPlatformShift(
                        ShiftId,
                        UserId,
                        SiteGroupId,
                        SiteId,
                        "ACTIVE",
                        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                        null)
                ],
                []));
    }
}
