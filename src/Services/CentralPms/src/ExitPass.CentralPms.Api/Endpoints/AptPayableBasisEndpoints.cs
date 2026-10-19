using System.Diagnostics;
using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
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
        ICentralPmsRbacRepository rbacRepository,
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

        var authority = await ResolveAptAuthorityAsync(
            request,
            rbacRepository,
            body.SiteId,
            body.TerminalId,
            body.CorrelationId,
            cancellationToken);
        if (authority.Error is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, authority.Error.Message);
            return Results.Json(authority.Error, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.ResolveAsync(
            body,
            ResolveCompletionActor(request, authority.TerminalIdentifier),
            cancellationToken);
        return ToHttpResult(result, request.HttpContext.Response);
    }

    private static async Task<IResult> RevalidateAsync(
        HttpRequest request,
        AptPayableBasisRevalidateRequest? body,
        IAptPayableBasisReadinessService service,
        ICentralPmsRbacRepository rbacRepository,
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

        var authority = await ResolveAptAuthorityAsync(
            request,
            rbacRepository,
            body.SiteId,
            body.TerminalId,
            body.CorrelationId,
            cancellationToken);
        if (authority.Error is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, authority.Error.Message);
            return Results.Json(authority.Error, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.RevalidateAsync(
            body,
            ResolveCompletionActor(request, authority.TerminalIdentifier),
            cancellationToken);
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

    public static async Task<ErrorResponse?> ValidateAptAuthorityAsync(
        HttpRequest request,
        ICentralPmsRbacRepository repository,
        string siteId,
        string terminalId,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        (await ResolveAptAuthorityAsync(
            request,
            repository,
            siteId,
            terminalId,
            correlationId,
            cancellationToken)).Error;

    /// <summary>
    /// Resolves an authenticated APT request to its canonical Device Administration terminal identifier.
    /// </summary>
    public static async Task<AptAuthorityResolution> ResolveAptAuthorityAsync(
        HttpRequest request,
        ICentralPmsRbacRepository repository,
        string siteId,
        string terminalId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var humanIdentities = request.HttpContext.User.Identities
            .Where(identity => identity.IsAuthenticated &&
                string.Equals(identity.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
            .ToArray();
        if (humanIdentities.Length == 0)
        {
            return new AptAuthorityResolution(null, null);
        }

        if (humanIdentities.Length != 1)
        {
            return new AptAuthorityResolution(BuildError(
                "APT_HUMAN_SESSION_AMBIGUOUS",
                "The authenticated APT human session is ambiguous.",
                correlationId,
                false), null);
        }

        var humanPrincipal = new ClaimsPrincipal(humanIdentities[0]);

        if (!Guid.TryParse(siteId, out var requestedSiteId) ||
            requestedSiteId == Guid.Empty ||
            !HasExactSiteScope(humanPrincipal, requestedSiteId))
        {
            return new AptAuthorityResolution(BuildError(
                "FORBIDDEN_SITE",
                "Caller is not authorized for the requested Site.",
                correlationId,
                false), null);
        }

        if (!Guid.TryParse(
                humanPrincipal.FindFirst("device_service_identity_id")?.Value,
                out var deviceServiceIdentityId) ||
            deviceServiceIdentityId == Guid.Empty)
        {
            return new AptAuthorityResolution(BuildError(
                "FORBIDDEN_TERMINAL",
                "The trusted terminal binding is unavailable.",
                correlationId,
                false), null);
        }

        var device = await repository.GetAptDeviceAuthorizationAsync(
            deviceServiceIdentityId,
            requestedSiteId,
            cancellationToken);
        if (device is null ||
            !device.Active ||
            !device.SiteAssigned ||
            !string.Equals(device.IdentityType, "DEVICE", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(device.OwningServiceName, "ExitPass.AssistedPaymentTerminal", StringComparison.Ordinal) ||
            !string.Equals(device.TerminalIdentifier, terminalId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new AptAuthorityResolution(BuildError(
                "FORBIDDEN_TERMINAL",
                "The trusted terminal is not authorized for the requested Site.",
                correlationId,
                false), null);
        }

        return new AptAuthorityResolution(null, device.TerminalIdentifier.Trim());
    }

    private static AptZeroPayableCompletionActor? ResolveCompletionActor(HttpRequest request, string? terminalIdentifier)
    {
        var humanIdentities = request.HttpContext.User.Identities
            .Where(identity => identity.IsAuthenticated &&
                string.Equals(identity.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
            .ToArray();
        if (humanIdentities.Length != 1)
            return null;

        var identity = humanIdentities[0];
        if (!string.Equals(identity.FindFirst("exitpass_audience")?.Value, "APT", StringComparison.Ordinal) ||
            !Guid.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ||
            userId == Guid.Empty)
        {
            return null;
        }

        var displayName = identity.FindFirst("display_name")?.Value;
        return string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(terminalIdentifier)
            ? null
            : new AptZeroPayableCompletionActor(userId, displayName.Trim(), terminalIdentifier.Trim());
    }

    /// <summary>
    /// Contains the fail-closed authorization result and canonical terminal identifier for an APT request.
    /// </summary>
    public sealed record AptAuthorityResolution(
        ErrorResponse? Error,
        string? TerminalIdentifier);

    private static bool HasExactSiteScope(System.Security.Claims.ClaimsPrincipal principal, Guid siteId)
    {
        var expected = siteId.ToString("D");
        return principal.FindAll("site_id").Any(claim =>
                   string.Equals(claim.Value, expected, StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(
                   principal.FindFirst("operator_effective_site_id")?.Value,
                   expected,
                   StringComparison.OrdinalIgnoreCase);
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
