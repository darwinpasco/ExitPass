using System.Security.Claims;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Enforces Central PMS operational RBAC policies declared by endpoint metadata.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - RBAC authorization is operational access control only and never mutates payment, provider, exit, gate, or settlement truth.
/// - Policy decisions are deterministic and correlation-aware.
/// </summary>
public sealed class CentralPmsRbacMiddleware
{
    private readonly RequestDelegate _next;

    public CentralPmsRbacMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<CentralPmsRbacOptions> options,
        ICentralPmsRbacRepository repository,
        IWebHostEnvironment environment,
        ILogger<CentralPmsRbacMiddleware> logger)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<ReconciliationPolicyMetadata>();
        if (metadata is null || !options.Value.Enabled)
        {
            await _next(context);
            return;
        }

        var policyName = metadata.PolicyName;
        var requiredPermissions = CentralPmsRbacPolicyCatalog.ResolvePermissions(policyName);
        var correlationId = ResolveCorrelationId(context);
        var fixtureHeadersAllowed = (environment.IsDevelopment() || environment.IsEnvironment("SecureDevelopment") || environment.IsEnvironment("Test")) && options.Value.AllowFixtureIdentityHeaders;
        var userId = ResolveGuid(context, fixtureHeadersAllowed ? CentralPmsRbacPolicyCatalog.UserIdHeaderName : null, ClaimTypes.NameIdentifier, "sub", "user_id");
        var serviceIdentityId = ResolveGuid(
            context,
            fixtureHeadersAllowed ? CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName : null,
            "service_identity_id",
            "client_id");

        var restrictedHumanSession =
            context.User.Identity?.IsAuthenticated == true &&
            (string.Equals(context.User.FindFirst("password_change_required")?.Value, "true", StringComparison.OrdinalIgnoreCase) ||
             (string.Equals(context.User.FindFirst("exitpass_audience")?.Value, "MANAGEMENT_PLATFORM", StringComparison.OrdinalIgnoreCase) &&
              string.Equals(context.User.FindFirst("privileged_account")?.Value, "true", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(context.User.FindFirst("mfa_satisfied")?.Value, "true", StringComparison.OrdinalIgnoreCase)));

        if (restrictedHumanSession)
        {
            await DenyAsync(context, repository, logger, StatusCodes.Status403Forbidden,
                "HUMAN_SESSION_ASSURANCE_REQUIRED",
                "The current human session does not satisfy the required authentication assurance.",
                policyName, userId, serviceIdentityId, correlationId);
            return;
        }

        if (userId is null && serviceIdentityId is null && !HasAnyPermissionHeader(context, requiredPermissions, fixtureHeadersAllowed && options.Value.AllowPermissionHeader))
        {
            await DenyAsync(
                context,
                repository,
                logger,
                StatusCodes.Status401Unauthorized,
                "CENTRAL_PMS_RBAC_UNAUTHENTICATED",
                "An authenticated Central PMS operator or service identity is required.",
                policyName,
                userId,
                serviceIdentityId,
                correlationId);
            return;
        }

        if (HasAnyClaimPermission(context.User, requiredPermissions) ||
            HasAnyPermissionHeader(context, requiredPermissions, fixtureHeadersAllowed && options.Value.AllowPermissionHeader))
        {
            await _next(context);
            return;
        }

        if (userId.HasValue &&
            await repository.UserHasAnyPermissionAsync(userId.Value, requiredPermissions, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        if (serviceIdentityId.HasValue &&
            await repository.ServiceIdentityIsActiveAsync(serviceIdentityId.Value, context.RequestAborted) &&
            IsServicePolicy(policyName))
        {
            await _next(context);
            return;
        }

        await DenyAsync(
            context,
            repository,
            logger,
            StatusCodes.Status403Forbidden,
            "CENTRAL_PMS_RBAC_FORBIDDEN",
            "The caller does not have the required Central PMS permission.",
            policyName,
            userId,
            serviceIdentityId,
            correlationId);
    }

    private static async Task DenyAsync(
        HttpContext context,
        ICentralPmsRbacRepository repository,
        ILogger logger,
        int statusCode,
        string errorCode,
        string message,
        string policyName,
        Guid? userId,
        Guid? serviceIdentityId,
        Guid? correlationId)
    {
        try
        {
            await repository.RecordDeniedAsync(
                policyName,
                userId,
                serviceIdentityId,
                correlationId,
                context.Request.Path.Value ?? string.Empty,
                context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record Central PMS RBAC denial audit evidence.");
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId ?? Guid.Empty,
            Retryable = false,
            Details = new Dictionary<string, object?>
            {
                ["policy"] = policyName
            }
        });
    }

    private static bool IsServicePolicy(string policyName) =>
        string.Equals(policyName, "EventOutboxDispatcher", StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyClaimPermission(ClaimsPrincipal principal, IReadOnlyList<string> requiredPermissions)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var permissionClaims = principal.Claims
            .Where(claim => string.Equals(claim.Type, CentralPmsRbacPolicyCatalog.PermissionClaimType, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "scope", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredPermissions.Any(permissionClaims.Contains);
    }

    private static bool HasAnyPermissionHeader(
        HttpContext context,
        IReadOnlyList<string> requiredPermissions,
        bool allowPermissionHeader)
    {
        if (!allowPermissionHeader ||
            !context.Request.Headers.TryGetValue(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, out var value))
        {
            return false;
        }

        var permissions = value.ToString()
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredPermissions.Any(permissions.Contains);
    }

    private static Guid? ResolveCorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-Id", out var value) &&
        Guid.TryParse(value.ToString(), out var correlationId)
            ? correlationId
            : null;

    private static Guid? ResolveGuid(HttpContext context, string? headerName, params string[] claimTypes)
    {
        if (headerName is not null && context.Request.Headers.TryGetValue(headerName, out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerGuid))
        {
            return headerGuid;
        }

        foreach (var claimType in claimTypes)
        {
            var claimValue = context.User.FindFirst(claimType)?.Value;
            if (Guid.TryParse(claimValue, out var claimGuid))
            {
                return claimGuid;
            }
        }

        return null;
    }
}
