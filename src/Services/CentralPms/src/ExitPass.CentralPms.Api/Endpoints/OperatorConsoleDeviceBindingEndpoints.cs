using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;

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

    private static IResult Failure(int status, string code, Guid correlationId) =>
        Results.Json(new ErrorResponse
        {
            ErrorCode = code,
            Message = "Operator Console device binding could not be established.",
            CorrelationId = correlationId,
            Retryable = false
        }, statusCode: status);
}
