using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public sealed record OperatorConsoleDeviceProofRequest(string Proof);

public static class OperatorConsoleDeviceBindingEndpoints
{
    public static IEndpointRouteBuilder MapOperatorConsoleDeviceBindingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/operator-console/device-binding/establish", EstablishAsync)
            .DisableAntiforgery()
            .WithTags("OperatorConsole")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary("Establish a server-issued Operator Console device-binding cookie")
            .WithDescription("Exchanges an opaque provisioned device proof for an HttpOnly same-origin cookie after resolving exactly one active canonical trusted-device record. No device, Site, shift, role, permission, or credential reference is returned.");
        app.MapPost("/v1/operator-console/device-binding/bind-session", BindSessionAsync)
            .RequireAuthorization()
            .WithTags("OperatorConsole")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary("Bind the authenticated Operator Console session to its trusted operating context")
            .WithDescription("Revalidates the server-issued device cookie, Site and Site Group scope, authorization epoch, and credential version before binding the current human session.");
        return app;
    }

    private static async Task<IResult> EstablishAsync(
        OperatorConsoleDeviceProofRequest body,
        HttpRequest request,
        HttpResponse response,
        IOperatorConsoleOperatingContextService service,
        IHumanAuthenticationOriginValidator originValidator,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(request);
        response.Headers.CacheControl = "no-store";
        if (!originValidator.IsAllowed(request) || string.IsNullOrWhiteSpace(body.Proof))
        {
            return Failure(StatusCodes.Status400BadRequest, OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid, correlationId);
        }

        var result = await service.EstablishDeviceBindingAsync(body.Proof, correlationId, cancellationToken);
        if (!result.Succeeded)
        {
            OperatorConsoleDeviceBindingCookie.Delete(response);
            return Failure(StatusCodes.Status403Forbidden, result.ErrorCode!, correlationId);
        }

        OperatorConsoleDeviceBindingCookie.Issue(response, result.CookieCredential!, timeProvider.GetUtcNow());
        return Results.NoContent();
    }

    private static async Task<IResult> BindSessionAsync(
        HttpRequest request,
        HttpResponse response,
        IOperatorConsoleOperatingContextService service,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(request);
        response.Headers.CacheControl = "no-store";
        try
        {
            await antiforgery.ValidateRequestAsync(request.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Failure(StatusCodes.Status403Forbidden, "HUMAN_SESSION_CSRF_INVALID", correlationId);
        }

        if (!Guid.TryParse(request.HttpContext.User.FindFirstValue(HumanSessionAuthenticationHandler.InternalHumanSessionIdClaimType), out var humanSessionId) ||
            humanSessionId == Guid.Empty ||
            !Guid.TryParse(request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId == Guid.Empty)
        {
            return Failure(StatusCodes.Status403Forbidden, OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked, correlationId);
        }

        var result = await service.BindSessionAsync(
            humanSessionId,
            userId,
            GuidClaims(request.HttpContext.User, "site_id"),
            GuidClaims(request.HttpContext.User, "site_group_id"),
            string.Equals(request.HttpContext.User.FindFirstValue("has_global_scope"), "true", StringComparison.OrdinalIgnoreCase),
            OperatorConsoleDeviceBindingCookie.Read(request),
            correlationId,
            cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : Failure(StatusCodes.Status403Forbidden, result.ErrorCode ?? OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked, correlationId);
    }

    private static Guid[] GuidClaims(ClaimsPrincipal principal, string claimType) =>
        principal.FindAll(claimType)
            .Select(claim => Guid.TryParse(claim.Value, out var value) ? value : Guid.Empty)
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToArray();

    private static IResult Failure(int status, string code, Guid correlationId) =>
        Results.Json(new ErrorResponse
        {
            ErrorCode = code,
            Message = "Operator Console device binding could not be established.",
            CorrelationId = correlationId,
            Retryable = false
        }, statusCode: status);
}
