using System.Security.Cryptography;
using System.Text;
using ExitPass.VendorPmsAdapter.Api.Configuration;

namespace ExitPass.VendorPmsAdapter.Api.Security;

public sealed class CentralPmsServiceAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SiteAdapterRuntimeOptions options)
    {
        if (!context.Request.Path.StartsWithSegments("/v1/vendor")) { await next(context); return; }
        var identityText = context.Request.Headers["X-ExitPass-Service-Identity"].ToString();
        var suppliedKey = context.Request.Headers["X-ExitPass-Adapter-Key"].ToString();
        var authenticated = Guid.TryParse(identityText, out var identityId) &&
            identityId == options.AllowedCentralPmsServiceIdentityId &&
            FixedTimeEquals(suppliedKey, SiteAdapterRuntimeOptions.ReadSecret(
                options.CentralPmsApiKeyFile!, options.SecretMountRoot!));
        if (!authenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new { code = "SITE_ADAPTER_AUTHENTICATION_REQUIRED",
                message = "Service authentication failed safely.", correlationId = CorrelationId(context) });
            return;
        }

        var requiredOperation = RequiredOperation(context.Request.Path);
        if (requiredOperation is null || !options.AllowsOperation(requiredOperation))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                code = "SITE_ADAPTER_PERMISSION_REQUIRED",
                message = "Service permission is insufficient for this operation.",
                correlationId = CorrelationId(context)
            });
            return;
        }
        await next(context);
    }

    private static string? RequiredOperation(PathString path)
    {
        if (path.Equals("/v1/vendor/identity")) return SiteAdapterOperations.IdentityRead;
        if (path.Equals("/v1/vendor/sessions/resolve")) return SiteAdapterOperations.SessionResolution;
        if (path.Equals("/v1/vendor/tariffs/calculate")) return SiteAdapterOperations.TariffCalculation;
        if (path.Equals("/v1/vendor/parking-fees/confirm")) return SiteAdapterOperations.PaymentConfirmation;
        if (path.Equals("/v1/vendor/passageway-records/synchronize"))
            return SiteAdapterOperations.PassagewaySynchronization;
        return null;
    }

    private static bool FixedTimeEquals(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? context.TraceIdentifier;
}
