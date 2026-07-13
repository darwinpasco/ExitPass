using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Read-only Management Platform identity/RBAC inventory endpoints.
/// </summary>
public static class ManagementPlatformIdentityRbacInventoryEndpoints
{
    private const string InventoryReadPolicy = "ManagementPlatformIdentityRbacInventoryRead";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ManagementPlatformIdentityRbacInventory");

    public static IEndpointRouteBuilder MapManagementPlatformIdentityRbacInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/management-platform")
            .WithTags("ManagementPlatform");

        group.MapGet("/identity-rbac/inventory", GetInventoryAsync)
            .WithName("GetManagementPlatformIdentityRbacInventory")
            .WithTags("ManagementPlatform")
            .WithMetadata(new ReconciliationPolicyMetadata(InventoryReadPolicy))
            .Produces<ManagementPlatformIdentityRbacInventoryResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Get read-only identity/RBAC inventory")
            .WithDescription("Returns a safe read-only inventory for future ExitPass Management Platform Identity & RBAC Administration. This endpoint does not create or mutate users, roles, permissions, assignments, statutory discounts, fiscal records, payment state, HikCentral state, gate/exit state, refund/reversal state, or rendered artifacts.");

        return app;
    }

    private static async Task<IResult> GetInventoryAsync(
        HttpRequest httpRequest,
        IManagementPlatformIdentityRbacInventoryService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetManagementPlatformIdentityRbacInventory", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementPlatformIdentityRbacInventoryEndpoints");
        var correlationId = ResolveRequestCorrelationId(httpRequest);

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);

        try
        {
            var inventory = await service.GetInventoryAsync(cancellationToken);
            activity?.SetTag("inventory_user_count", inventory.Users.Count);
            activity?.SetTag("inventory_role_bundle_count", inventory.RoleBundles.Count);
            activity?.SetTag("inventory_permission_count", inventory.Permissions.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Results.Ok(ToContract(inventory));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Management Platform identity/RBAC inventory read failed.");

            return Results.Json(
                new ErrorResponse
                {
                    ErrorCode = "MANAGEMENT_PLATFORM_IDENTITY_RBAC_INVENTORY_READ_FAILED",
                    Message = "The identity/RBAC inventory could not be loaded.",
                    CorrelationId = correlationId,
                    Retryable = false
                },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static ManagementPlatformIdentityRbacInventoryResponse ToContract(
        ManagementPlatformIdentityRbacInventory inventory) =>
        new(
            inventory.Users.Select(user => new ManagementPlatformIdentityUserDto(
                user.UserId,
                user.Username,
                user.DisplayName,
                user.Email,
                user.Status,
                user.SourceSystem,
                user.CreatedAt,
                user.UpdatedAt)).ToArray(),
            inventory.RoleBundles.Select(role => new ManagementPlatformRoleBundleDto(
                role.RoleKey,
                role.DisplayName,
                role.Purpose,
                role.TypicalAccessRights,
                role.DefaultRestrictions,
                role.TargetSurface)).ToArray(),
            inventory.Permissions.Select(permission => new ManagementPlatformPermissionDto(
                permission.PermissionKey,
                permission.DisplayLabel,
                permission.Category,
                permission.SourceCatalog,
                permission.MappedPolicies,
                permission.Status,
                permission.Notes)).ToArray(),
            inventory.PolicyMappings.Select(mapping => new ManagementPlatformPolicyMappingDto(
                mapping.PolicyName,
                mapping.Permissions,
                mapping.RouteOrFeatureArea,
                mapping.ImplementedStatus,
                mapping.Notes)).ToArray(),
            inventory.UserRoleAssignments.Select(assignment => new ManagementPlatformUserRoleAssignmentDto(
                assignment.UserId,
                assignment.RoleId,
                assignment.RoleKey,
                assignment.RoleName,
                assignment.RoleStatus,
                assignment.AssignmentStatus,
                assignment.EffectiveFrom,
                assignment.EffectiveTo)).ToArray(),
            inventory.UserSiteScopes.Select(scope => new ManagementPlatformUserSiteScopeDto(
                scope.UserId,
                scope.SiteGroupId,
                scope.SiteId,
                scope.SiteGroupName,
                scope.SiteName,
                scope.Source,
                scope.Status)).ToArray(),
            inventory.DeviceBindings.Select(binding => new ManagementPlatformDeviceBindingDto(
                binding.DeviceBindingId,
                binding.DeviceLabel,
                binding.AssignedUserId,
                binding.SiteGroupId,
                binding.SiteId,
                binding.Status,
                binding.TrustStatus,
                binding.LastSeenAt)).ToArray(),
            inventory.Shifts.Select(shift => new ManagementPlatformShiftDto(
                shift.ShiftId,
                shift.OperatorUserId,
                shift.SiteGroupId,
                shift.SiteId,
                shift.Status,
                shift.StartedAt,
                shift.EndedAt)).ToArray(),
            inventory.Gaps.Select(gap => new ManagementPlatformInventoryGapDto(
                gap.GapKey,
                gap.Severity,
                gap.Summary)).ToArray(),
            inventory.GeneratedAt);

    private static Guid ResolveRequestCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerCorrelationId) &&
            headerCorrelationId != Guid.Empty)
        {
            return headerCorrelationId;
        }

        return Guid.NewGuid();
    }
}
