namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed record ManagementPlatformIdentityRbacInventory(
    IReadOnlyList<ManagementPlatformIdentityUser> Users,
    IReadOnlyList<ManagementPlatformRoleBundle> RoleBundles,
    IReadOnlyList<ManagementPlatformPermission> Permissions,
    IReadOnlyList<ManagementPlatformPolicyMapping> PolicyMappings,
    IReadOnlyList<ManagementPlatformUserRoleAssignment> UserRoleAssignments,
    IReadOnlyList<ManagementPlatformUserSiteScope> UserSiteScopes,
    IReadOnlyList<ManagementPlatformDeviceBinding> DeviceBindings,
    IReadOnlyList<ManagementPlatformShift> Shifts,
    IReadOnlyList<ManagementPlatformInventoryGap> Gaps,
    DateTimeOffset GeneratedAt);

public sealed record ManagementPlatformIdentityRbacPersistenceInventory(
    IReadOnlyList<ManagementPlatformIdentityUser> Users,
    IReadOnlyList<ManagementPlatformUserRoleAssignment> UserRoleAssignments,
    IReadOnlyList<ManagementPlatformUserSiteScope> UserSiteScopes,
    IReadOnlyList<ManagementPlatformDeviceBinding> DeviceBindings,
    IReadOnlyList<ManagementPlatformShift> Shifts,
    IReadOnlyList<ManagementPlatformInventoryGap> Gaps);

public sealed record ManagementPlatformIdentityUser(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    string Status,
    string SourceSystem,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ManagementPlatformRoleBundle(
    string RoleKey,
    string DisplayName,
    string Purpose,
    IReadOnlyList<string> TypicalAccessRights,
    IReadOnlyList<string> DefaultRestrictions,
    string TargetSurface);

public sealed record ManagementPlatformPermission(
    string PermissionKey,
    string DisplayLabel,
    string Category,
    string SourceCatalog,
    IReadOnlyList<string> MappedPolicies,
    string Status,
    string? Notes);

public sealed record ManagementPlatformPolicyMapping(
    string PolicyName,
    IReadOnlyList<string> Permissions,
    string RouteOrFeatureArea,
    string ImplementedStatus,
    string? Notes);

public sealed record ManagementPlatformUserRoleAssignment(
    Guid UserId,
    Guid? RoleId,
    string RoleKey,
    string RoleName,
    string RoleStatus,
    string AssignmentStatus,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record ManagementPlatformUserSiteScope(
    Guid UserId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string SiteGroupName,
    string SiteName,
    string Source,
    string Status);

public sealed record ManagementPlatformDeviceBinding(
    Guid DeviceBindingId,
    string DeviceLabel,
    Guid? AssignedUserId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string Status,
    string TrustStatus,
    DateTimeOffset? LastSeenAt);

public sealed record ManagementPlatformShift(
    Guid ShiftId,
    Guid? OperatorUserId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);

public sealed record ManagementPlatformInventoryGap(
    string GapKey,
    string Severity,
    string Summary);
