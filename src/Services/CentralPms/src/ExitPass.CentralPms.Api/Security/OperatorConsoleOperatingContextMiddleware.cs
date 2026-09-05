using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;

namespace ExitPass.CentralPms.Api.Security;

public sealed class OperatorConsoleOperatingContextMiddleware(RequestDelegate next, ILogger<OperatorConsoleOperatingContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IOperatorConsoleOperatingContextService service)
    {
        if (!IsOperatorConsoleRequest(context.Request.Path) ||
            !string.Equals(context.User.Identity?.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(context.Request);
        if (!Guid.TryParse(context.User.FindFirst(HumanSessionAuthenticationHandler.InternalHumanSessionIdClaimType)?.Value, out var humanSessionId) ||
            humanSessionId == Guid.Empty)
        {
            await DenyAsync(context, OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked, correlationId);
            return;
        }

        var result = await service.ValidateSessionAsync(
            humanSessionId,
            OperatorConsoleDeviceBindingCookie.Read(context.Request),
            correlationId,
            context.RequestAborted);
        if (!result.Succeeded || result.Context is null)
        {
            logger.LogWarning(
                "Operator Console readiness denial {Classification} for session {HumanSessionId} and correlation {CorrelationId}.",
                result.ErrorCode,
                humanSessionId,
                correlationId);
            await DenyAsync(context, result.ErrorCode ?? OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked, correlationId);
            return;
        }

        OperatorConsoleDeviceBindingCookie.AddClaims(context.User, result.Context);
        await next(context);
    }

    private static bool IsOperatorConsoleRequest(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.StartsWith("/v1/operator-console/shift-management", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return value.StartsWith("/v1/operator-console/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/v1/ops/operator-console/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DenyAsync(HttpContext context, string code, Guid correlationId)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            ErrorCode = code,
            Message = "Operator Console operating context is not ready.",
            CorrelationId = correlationId,
            Retryable = false
        }, context.RequestAborted);
    }
}
