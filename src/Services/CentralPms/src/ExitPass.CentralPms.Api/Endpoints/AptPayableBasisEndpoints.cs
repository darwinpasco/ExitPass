using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// APT-facing payable-basis readiness facade endpoints.
/// </summary>
public static class AptPayableBasisEndpoints
{
    public const string ReadPolicy = "TerminalCashPayableBasisRead";

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.TerminalCashPayableBasis");

    public static IEndpointRouteBuilder MapAptPayableBasisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/terminal-cash-payments/payable-basis")
            .WithTags("TerminalCashPayments");

        group.MapPost("/resolve", ResolveAsync)
            .WithName("ResolveTerminalCashPayableBasis")
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<AptPayableBasisReadinessResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/revalidate", RevalidateAsync)
            .WithName("RevalidateTerminalCashPayableBasis")
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<AptPayableBasisReadinessResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ResolveAsync(
        HttpRequest request,
        AptPayableBasisResolveRequest? body,
        IAptPayableBasisReadinessService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ResolveTerminalCashPayableBasis", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/terminal-cash-payments/payable-basis/resolve");

        if (body is null)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "Request body is required.", Guid.Empty, false));
        }

        if (!SiteScopeAllowed(request, body.SiteId, body.CorrelationId, out var forbidden))
        {
            activity?.SetStatus(ActivityStatusCode.Error, forbidden!.Message);
            return Results.Json(forbidden, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.ResolveAsync(body, cancellationToken);
        return ToHttpResult(result, request.HttpContext.Response);
    }

    private static async Task<IResult> RevalidateAsync(
        HttpRequest request,
        AptPayableBasisRevalidateRequest? body,
        IAptPayableBasisReadinessService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("RevalidateTerminalCashPayableBasis", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/terminal-cash-payments/payable-basis/revalidate");

        if (body is null)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "Request body is required.", Guid.Empty, false));
        }

        if (!SiteScopeAllowed(request, body.SiteId, body.CorrelationId, out var forbidden))
        {
            activity?.SetStatus(ActivityStatusCode.Error, forbidden!.Message);
            return Results.Json(forbidden, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.RevalidateAsync(body, cancellationToken);
        return ToHttpResult(result, request.HttpContext.Response);
    }

    private static IResult ToHttpResult(
        AptPayableBasisReadinessResult result,
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
                result.ErrorCode ?? "APT_PAYABLE_BASIS_FAILED",
                result.Message ?? "APT payable-basis readiness failed.",
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
                "APT payable-basis request contains malformed Site ID.",
                correlationId,
                false);
            return false;
        }

        if (!request.Headers.TryGetValue("X-Site-Id", out var header) ||
            !Guid.TryParse(header.ToString(), out var authorizedSiteId) ||
            authorizedSiteId != requestSiteId)
        {
            error = BuildError(
                "FORBIDDEN_SITE",
                "Caller is not authorized for the requested Site.",
                correlationId,
                false);
            return false;
        }

        return true;
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
}
