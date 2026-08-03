using System.Diagnostics;
using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class AptStatutoryOrdinanceAvailabilityEndpoints
{
    public const string ReadPolicy = AptStatutoryOrdinanceAvailabilityValues.PolicyName;

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.AptStatutoryOrdinanceAvailability");

    public static IEndpointRouteBuilder MapAptStatutoryOrdinanceAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/apt/statutory-discounts/ordinance-availability")
            .WithTags("AptStatutoryDiscounts");

        group.MapPost("/resolve", ResolveAsync)
            .WithName("ResolveAptStatutoryOrdinanceAvailability")
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<AptStatutoryOrdinanceAvailabilityResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/revalidate", RevalidateAsync)
            .WithName("RevalidateAptStatutoryOrdinanceAvailability")
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<AptStatutoryOrdinanceAvailabilityResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ResolveAsync(
        HttpRequest request,
        AptStatutoryOrdinanceAvailabilityRequest? body,
        IAptStatutoryOrdinanceAvailabilityService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ResolveAptStatutoryOrdinanceAvailability", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/apt/statutory-discounts/ordinance-availability/resolve");

        if (!ValidateEnvelope(request, body, out var error))
        {
            activity?.SetStatus(ActivityStatusCode.Error, error!.Message);
            return Results.Json(error, statusCode: ResolveFailureStatus(error.ErrorCode));
        }

        var result = await service.ResolveAsync(body!, cancellationToken);
        return ToHttpResult(result, request.HttpContext.Response);
    }

    private static async Task<IResult> RevalidateAsync(
        HttpRequest request,
        AptStatutoryOrdinanceAvailabilityRequest? body,
        IAptStatutoryOrdinanceAvailabilityService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("RevalidateAptStatutoryOrdinanceAvailability", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/apt/statutory-discounts/ordinance-availability/revalidate");

        if (!ValidateEnvelope(request, body, out var error))
        {
            activity?.SetStatus(ActivityStatusCode.Error, error!.Message);
            return Results.Json(error, statusCode: ResolveFailureStatus(error.ErrorCode));
        }

        var result = await service.RevalidateAsync(body!, cancellationToken);
        return ToHttpResult(result, request.HttpContext.Response);
    }

    private static bool ValidateEnvelope(
        HttpRequest request,
        AptStatutoryOrdinanceAvailabilityRequest? body,
        out ErrorResponse? error)
    {
        error = null;
        if (body is null)
        {
            error = BuildError("INVALID_REQUEST", "Request body is required.", Guid.Empty, false);
            return false;
        }

        if (!HasServiceIdentity(request.HttpContext))
        {
            error = BuildError(
                "CENTRAL_PMS_SERVICE_IDENTITY_REQUIRED",
                "An authenticated APT service identity is required.",
                body.CorrelationId,
                false);
            return false;
        }

        if (!SiteScopeAllowed(request, body.SiteId, body.CorrelationId, out error))
        {
            return false;
        }

        return true;
    }

    private static IResult ToHttpResult(
        AptStatutoryOrdinanceAvailabilityResult result,
        HttpResponse response)
    {
        if (result.CorrelationId != Guid.Empty)
        {
            response.Headers["X-Correlation-Id"] = result.CorrelationId.ToString("D");
        }

        if (result.Succeeded && result.Response is not null)
        {
            return Results.Ok(result.Response);
        }

        return Results.Json(
            BuildError(
                result.ErrorCode ?? AptStatutoryOrdinanceAvailabilityValues.UnexpectedFailure,
                result.Message ?? "APT statutory ordinance availability failed.",
                result.CorrelationId,
                result.Retryable),
            statusCode: result.HttpStatusCode);
    }

    private static bool SiteScopeAllowed(
        HttpRequest request,
        string siteId,
        Guid correlationId,
        out ErrorResponse? error)
    {
        error = null;
        if (!Guid.TryParse(siteId, out var requestSiteId) || requestSiteId == Guid.Empty)
        {
            error = BuildError(
                "INVALID_REQUEST",
                "APT ordinance availability request contains malformed Site ID.",
                correlationId,
                false);
            return false;
        }

        if (!request.Headers.TryGetValue("X-Site-Id", out var header) ||
            !Guid.TryParse(header.ToString(), out var authorizedSiteId) ||
            authorizedSiteId != requestSiteId)
        {
            error = BuildError(
                AptStatutoryOrdinanceAvailabilityValues.AccessDenied,
                "Caller is not authorized for the requested Site.",
                correlationId,
                false);
            return false;
        }

        return true;
    }

    private static bool HasServiceIdentity(HttpContext context) =>
        ResolveGuid(context, CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, "service_identity_id", "client_id") is not null;

    private static Guid? ResolveGuid(HttpContext context, string headerName, params string[] claimTypes)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerGuid))
        {
            return headerGuid;
        }

        foreach (var claimType in claimTypes)
        {
            var claimValue = context.User.FindFirstValue(claimType);
            if (Guid.TryParse(claimValue, out var claimGuid))
            {
                return claimGuid;
            }
        }

        return null;
    }

    private static ErrorResponse BuildError(
        string code,
        string message,
        Guid correlationId,
        bool retryable) =>
        new()
        {
            ErrorCode = code,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };

    private static int ResolveFailureStatus(string errorCode) =>
        errorCode switch
        {
            "INVALID_REQUEST" => StatusCodes.Status400BadRequest,
            "CENTRAL_PMS_SERVICE_IDENTITY_REQUIRED" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status403Forbidden
        };
}
