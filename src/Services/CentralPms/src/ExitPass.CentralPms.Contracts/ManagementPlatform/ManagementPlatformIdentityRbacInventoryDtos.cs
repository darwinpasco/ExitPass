namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

/// <summary>
/// Read-only identity/RBAC inventory for the ExitPass Management Platform.
/// </summary>
public sealed record ManagementPlatformIdentityRbacInventoryResponse(
    IReadOnlyList<ManagementPlatformIdentityUserDto> Users,
    IReadOnlyList<ManagementPlatformRoleBundleDto> RoleBundles,
    IReadOnlyList<ManagementPlatformPermissionDto> Permissions,
    IReadOnlyList<ManagementPlatformPolicyMappingDto> PolicyMappings,
    IReadOnlyList<ManagementPlatformUserRoleAssignmentDto> UserRoleAssignments,
    IReadOnlyList<ManagementPlatformUserSiteScopeDto> UserSiteScopes,
    IReadOnlyList<ManagementPlatformDeviceBindingDto> DeviceBindings,
    IReadOnlyList<ManagementPlatformShiftDto> Shifts,
    IReadOnlyList<ManagementPlatformInventoryGapDto> Gaps,
    DateTimeOffset GeneratedAt);

public sealed record ManagementPlatformIdentityUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    string Status,
    string SourceSystem,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ManagementPlatformRoleBundleDto(
    string RoleKey,
    string DisplayName,
    string Purpose,
    IReadOnlyList<string> TypicalAccessRights,
    IReadOnlyList<string> DefaultRestrictions,
    string TargetSurface);

public sealed record ManagementPlatformPermissionDto(
    string PermissionKey,
    string DisplayLabel,
    string Category,
    string SourceCatalog,
    IReadOnlyList<string> MappedPolicies,
    string Status,
    string? Notes);

public sealed record ManagementPlatformPolicyMappingDto(
    string PolicyName,
    IReadOnlyList<string> Permissions,
    string RouteOrFeatureArea,
    string ImplementedStatus,
    string? Notes);

public sealed record ManagementPlatformUserRoleAssignmentDto(
    Guid UserId,
    Guid? RoleId,
    string RoleKey,
    string RoleName,
    string RoleStatus,
    string AssignmentStatus,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record ManagementPlatformUserSiteScopeDto(
    Guid UserId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string SiteGroupName,
    string SiteName,
    string Source,
    string Status);

public sealed record ManagementPlatformDeviceBindingDto(
    Guid DeviceBindingId,
    string DeviceLabel,
    Guid? AssignedUserId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string Status,
    string TrustStatus,
    DateTimeOffset? LastSeenAt);

public sealed record ManagementPlatformShiftDto(
    Guid ShiftId,
    Guid? OperatorUserId,
    Guid? SiteGroupId,
    Guid? SiteId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);

public sealed record ManagementPlatformInventoryGapDto(
    string GapKey,
    string Severity,
    string Summary);
