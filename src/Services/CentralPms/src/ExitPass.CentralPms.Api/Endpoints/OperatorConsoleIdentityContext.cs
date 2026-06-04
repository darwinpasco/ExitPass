using System.Security.Claims;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Centralized Operator Console identity and local-development header fallback parsing.
/// </summary>
internal sealed record OperatorConsoleIdentityContext(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid CorrelationId)
{
    private const string UserIdHeader = "X-Operator-User-Id";
    private const string DeviceBindingIdHeader = "X-Operator-Device-Binding-Id";
    private const string ShiftIdHeader = "X-Operator-Shift-Id";
    private const string SiteIdHeader = "X-Site-Id";
    private const string SiteGroupIdHeader = "X-Site-Group-Id";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public static OperatorConsoleIdentityContext Resolve(
        HttpRequest request,
        Guid? fallbackUserId = null,
        Guid? fallbackOperatorDeviceBindingId = null,
        Guid? fallbackOperatorShiftId = null,
        Guid? fallbackSiteId = null,
        Guid? fallbackSiteGroupId = null,
        Guid? fallbackCorrelationId = null)
    {
        var userId = ResolveGuid(
            request,
            UserIdHeader,
            fallbackUserId,
            ClaimTypes.NameIdentifier,
            "sub",
            "user_id");

        if (!userId.HasValue)
        {
            throw new ArgumentException("Operator user identity is required.", UserIdHeader);
        }

        var deviceBindingId = ResolveGuid(
            request,
            DeviceBindingIdHeader,
            fallbackOperatorDeviceBindingId,
            "operator_device_binding_id");

        var shiftId = ResolveGuid(
            request,
            ShiftIdHeader,
            fallbackOperatorShiftId,
            "operator_shift_id");

        var siteId = ResolveGuid(
            request,
            SiteIdHeader,
            fallbackSiteId,
            "site_id");

        var siteGroupId = ResolveGuid(
            request,
            SiteGroupIdHeader,
            fallbackSiteGroupId,
            "site_group_id");

        var correlationId = ResolveGuid(
            request,
            CorrelationIdHeader,
            fallbackCorrelationId,
            "correlation_id") ?? Guid.NewGuid();

        return new OperatorConsoleIdentityContext(
            userId.Value,
            deviceBindingId,
            shiftId,
            siteId,
            siteGroupId,
            correlationId);
    }

    private static Guid? ResolveGuid(
        HttpRequest request,
        string headerName,
        Guid? fallback,
        params string[] claimTypes)
    {
        Guid? resolved = null;
        var source = "fallback";

        foreach (var claimType in claimTypes)
        {
            var claimValue = request.HttpContext.User.FindFirst(claimType)?.Value;
            if (Guid.TryParse(claimValue, out var claimGuid) && claimGuid != Guid.Empty)
            {
                resolved = claimGuid;
                source = $"claim:{claimType}";
                break;
            }
        }

        if (request.Headers.TryGetValue(headerName, out var headerValue) &&
            !string.IsNullOrWhiteSpace(headerValue.ToString()))
        {
            if (!Guid.TryParse(headerValue.ToString(), out var headerGuid) || headerGuid == Guid.Empty)
            {
                throw new ArgumentException($"{headerName} header must be a valid GUID.", headerName);
            }

            if (resolved.HasValue && resolved.Value != headerGuid)
            {
                throw new ArgumentException($"{headerName} header does not match authenticated Operator Console identity.", headerName);
            }

            resolved = headerGuid;
            source = $"header:{headerName}";
        }

        if (fallback.HasValue && fallback.Value != Guid.Empty)
        {
            if (resolved.HasValue && resolved.Value != fallback.Value)
            {
                throw new ArgumentException($"{headerName} does not match Operator Console request identity from {source}.", headerName);
            }

            resolved ??= fallback.Value;
        }

        return resolved;
    }
}
