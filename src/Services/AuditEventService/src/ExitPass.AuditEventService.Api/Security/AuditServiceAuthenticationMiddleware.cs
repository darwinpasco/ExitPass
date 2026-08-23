using System.Security.Cryptography;
using System.Text;
using ExitPass.AuditEventService.Api.Configuration;
using ExitPass.AuditEventService.Contracts;

namespace ExitPass.AuditEventService.Api.Security;

public sealed class AuditServiceAuthenticationMiddleware(RequestDelegate next)
{
    public const string AuthenticatedIdentityItem = "AuditAuthenticatedServiceIdentity";

    public async Task InvokeAsync(HttpContext context, AuditEventServiceOptions options)
    {
        if (!context.Request.Path.StartsWithSegments("/v1/audit"))
        {
            await next(context);
            return;
        }

        var identityValue = context.Request.Headers["X-ExitPass-Service-Identity"].ToString();
        var suppliedKey = context.Request.Headers["X-ExitPass-Audit-Key"].ToString();
        var expectedKey = string.Empty;
        try { expectedKey = options.ReadApiKey(); }
        catch (InvalidOperationException) { }
        var authenticated = Guid.TryParse(identityValue, out var identityId) &&
            identityId == options.ServiceIdentityId && !string.IsNullOrEmpty(expectedKey) &&
            FixedTimeEquals(suppliedKey, expectedKey);
        if (!authenticated)
        {
            await WriteProblem(context, StatusCodes.Status401Unauthorized, "AUDIT_AUTHENTICATION_REQUIRED",
                "Service authentication failed safely.");
            return;
        }

        var requiredOperation = context.Request.Method == HttpMethods.Post
            ? AuditEventOperations.Append
            : context.Request.Method == HttpMethods.Get ? AuditEventOperations.Read : null;
        if (requiredOperation is null || !options.Allows(requiredOperation))
        {
            await WriteProblem(context, StatusCodes.Status403Forbidden, "AUDIT_PERMISSION_REQUIRED",
                "Service permission is insufficient for this operation.");
            return;
        }

        context.Items[AuthenticatedIdentityItem] = identityId;
        await next(context);
    }

    private static bool FixedTimeEquals(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static Task WriteProblem(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(new AuditProblem(code, message, CorrelationId(context)));
    }

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? context.TraceIdentifier;
}
