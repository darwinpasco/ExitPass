using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Security;

public sealed class ProductionFixtureIdentityHeaderGuardMiddleware
{
    private static readonly string[] HumanAuthorityHeaders =
    [
        CentralPmsRbacPolicyCatalog.UserIdHeaderName,
        CentralPmsRbacPolicyCatalog.PermissionsHeaderName,
        "X-Operator-User-Id"
    ];
    private readonly RequestDelegate _next;

    public ProductionFixtureIdentityHeaderGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IWebHostEnvironment environment, IOptions<CentralPmsRbacOptions> options)
    {
        var fixtureComposition = (environment.IsDevelopment() || environment.IsEnvironment("SecureDevelopment") || environment.IsEnvironment("Test")) && options.Value.AllowFixtureIdentityHeaders;
        if (!fixtureComposition && HumanAuthorityHeaders.Any(context.Request.Headers.ContainsKey))
        {
            var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(context.Request);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                ErrorCode = "FIXTURE_IDENTITY_HEADER_PROHIBITED",
                Message = "Caller-authored human identity and permission headers are not accepted.",
                CorrelationId = correlationId,
                Retryable = false
            });
            return;
        }
        await _next(context);
    }
}
