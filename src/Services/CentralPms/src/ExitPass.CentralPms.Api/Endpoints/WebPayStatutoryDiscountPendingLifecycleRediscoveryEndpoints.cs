using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.WebPay;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.WebPay;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// WebPay-facing read-only rediscovery endpoint for existing statutory pending lifecycles.
/// </summary>
public static class WebPayStatutoryDiscountPendingLifecycleRediscoveryEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.WebPayStatutoryDiscountPendingLifecycleRediscovery");

    public static IEndpointRouteBuilder MapWebPayStatutoryDiscountPendingLifecycleRediscoveryEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/webpay/statutory-discounts")
            .WithTags("WebPay");

        group.MapPost("/pending-lifecycle/rediscover", RediscoverAsync)
            .WithName("RediscoverWebPayStatutoryDiscountPendingLifecycle")
            .WithMetadata(new ReconciliationPolicyMetadata(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.PolicyName))
            .Produces<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> RediscoverAsync(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest body,
        HttpContext context,
        IWebPayStatutoryDiscountPendingLifecycleRediscoveryService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("RediscoverWebPayStatutoryDiscountPendingLifecycle", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/webpay/statutory-discounts/pending-lifecycle/rediscover");

        var correlationId = ReadOrCreateCorrelationId(context.Request);
        activity?.SetTag("correlation_id", correlationId);

        if (!TryValidateWebPayServicePrincipal(context.Request, out var principalError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "WebPay service principal is required.");
            return Results.Json(
                BuildError(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied,
                    principalError!,
                    correlationId,
                    retryable: false),
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var query = ToQuery(body, correlationId);
            activity?.SetTag("lookup_mode", query.LookupMode);
            activity?.SetTag("site_id", query.SiteId);
            activity?.SetTag("site_group_id", query.SiteGroupId);

            var result = await service.RediscoverAsync(query, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("rediscovery_classification", result.Classification);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Results.Ok(ToResponse(result));
        }
        catch (WebPayStatutoryDiscountPendingLifecycleRediscoveryRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unexpected statutory pending-lifecycle rediscovery failure.");
            return Results.Json(
                BuildError(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure,
                    "The parking privilege request could not be checked right now. Please try again.",
                    correlationId,
                    retryable: false),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery ToQuery(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest body,
        Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(body);

        return new WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery(
            Normalize(body.LookupMode),
            body.ParkingSessionId,
            body.SiteId ?? Guid.Empty,
            body.SiteGroupId ?? Guid.Empty,
            NormalizeOptional(body.TicketReference),
            NormalizeOptional(body.PlateNumber),
            NormalizeOptional(body.VendorSystemId),
            NormalizeOptional(body.EntitlementType),
            correlationId);
    }

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse ToResponse(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryResult result)
    {
        var lifecycle = result.Lifecycle;
        return new WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse(
            result.Classification,
            lifecycle?.StatutoryDecisionId,
            lifecycle?.StatutoryDecisionCommandId,
            lifecycle?.RequestReference,
            lifecycle?.EntitlementType,
            lifecycle?.DecisionStatus,
            lifecycle?.PayableBasisStatus,
            lifecycle?.ParkingSessionId,
            lifecycle?.SiteId,
            lifecycle?.SiteGroupId,
            lifecycle?.OpaqueContinuationReference,
            lifecycle?.OpaqueContinuationUrl,
            lifecycle?.LifecycleState ?? result.Classification,
            result.Retryable,
            result.CorrelationId,
            lifecycle?.CreatedAt,
            lifecycle?.UpdatedAt,
            lifecycle?.SubmittedAt,
            lifecycle?.DecidedAt,
            lifecycle?.ReviewedAt);
    }

    private static Guid ReadOrCreateCorrelationId(HttpRequest request)
    {
        var raw = request.Headers["X-Correlation-Id"].FirstOrDefault();
        return Guid.TryParse(raw, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
    }

    private static bool TryValidateWebPayServicePrincipal(HttpRequest request, out string? error)
    {
        error = null;
        var rawServiceIdentity = request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName].FirstOrDefault();
        if (!Guid.TryParse(rawServiceIdentity, out var serviceIdentityId) || serviceIdentityId == Guid.Empty)
        {
            error = "A WebPay service identity is required.";
            return false;
        }

        if (request.Headers.ContainsKey(CentralPmsRbacPolicyCatalog.UserIdHeaderName))
        {
            error = "A human operator identity cannot use the WebPay statutory recovery endpoint.";
            return false;
        }

        return true;
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId, bool retryable) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
