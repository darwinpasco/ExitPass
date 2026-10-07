using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class OperatorConsoleCanonicalSessionEndpoints
{
    public static IEndpointRouteBuilder MapOperatorConsoleCanonicalSessionEndpoints(this IEndpointRouteBuilder app)
    {
        MapPurposeEndpoint(
            app,
            "/v1/ops/operator-console/sessions/ensure-for-statutory-discount",
            "EnsureOperatorConsoleCanonicalSessionForStatutoryDiscount",
            "OperatorConsoleStatutoryDiscountDraftCreate",
            operatingContextRequired: true);
        MapPurposeEndpoint(
            app,
            "/v1/ops/operator-console/sessions/ensure-for-invoice-customer-information",
            "EnsureOperatorConsoleCanonicalSessionForInvoiceCustomerInformation",
            OperatorConsoleInvoiceCustomerInformationEndpoints.ManagePolicy,
            operatingContextRequired: false);
        return app;
    }

    private static void MapPurposeEndpoint(
        IEndpointRouteBuilder app,
        string path,
        string name,
        string policy,
        bool operatingContextRequired)
    {
        var endpoint = app.MapPost(path, EnsureAsync)
            .WithName(name)
            .WithTags("OperatorConsole")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Accepts<OperatorConsoleCanonicalSessionRequest>("application/json")
            .Produces<OperatorConsoleCanonicalSessionResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        if (!operatingContextRequired)
        {
            endpoint.WithMetadata(OperatorConsoleOperatingContextRequirementMetadata.NotRequired);
        }
    }

    private static async Task<IResult> EnsureAsync(
        OperatorConsoleCanonicalSessionRequest body,
        HttpRequest request,
        IOperatorConsoleCanonicalSessionService service,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(request.HttpContext.User.Identity?.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
            {
                await antiforgery.ValidateRequestAsync(request.HttpContext);
            }
        }
        catch (AntiforgeryValidationException)
        {
            return Error(StatusCodes.Status403Forbidden, "HUMAN_SESSION_CSRF_INVALID", "The CSRF token is missing or invalid.", Correlation(request, body.CorrelationId));
        }

        OperatorConsoleIdentityContext identity;
        try
        {
            identity = OperatorConsoleIdentityContext.Resolve(request);
            if (!identity.SiteId.HasValue || !identity.SiteGroupId.HasValue)
            {
                throw new ArgumentException("The authenticated Site and Site Group scope are required.");
            }

            var result = await service.EnsureAsync(
                new OperatorConsoleCanonicalSessionCommand(
                    identity.SiteId.Value,
                    identity.SiteGroupId.Value,
                    body.VendorSystemId,
                    body.LookupMode,
                    body.TicketReference,
                    body.PlateNumber,
                    identity.CorrelationId),
                cancellationToken);

            return Results.Ok(new OperatorConsoleCanonicalSessionResponse(
                result.ParkingSessionId,
                result.ReusedExistingSession,
                result.VendorSessionProjectionId,
                result.CorrelationId));
        }
        catch (ArgumentException exception)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_OPERATOR_CANONICAL_SESSION_REQUEST", exception.Message, Correlation(request, body.CorrelationId));
        }
        catch (OperatorConsoleCanonicalSessionException exception)
        {
            return Error(StatusCodes.Status409Conflict, exception.ErrorCode, "The projected parking session is not available for this operator action.", Correlation(request, body.CorrelationId));
        }
    }

    private static IResult Error(int statusCode, string code, string message, Guid correlationId) =>
        Results.Json(new ErrorResponse
        {
            ErrorCode = code,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        }, statusCode: statusCode);

    private static Guid Correlation(HttpRequest request, Guid bodyCorrelationId) =>
        Guid.TryParse(request.Headers["X-Correlation-Id"], out var header) && header != Guid.Empty
            ? header
            : bodyCorrelationId != Guid.Empty ? bodyCorrelationId : Guid.NewGuid();
}
