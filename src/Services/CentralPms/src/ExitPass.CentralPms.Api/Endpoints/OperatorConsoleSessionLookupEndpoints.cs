using System.Diagnostics;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console read-only session lookup endpoint.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This endpoint persists Operator Console access evaluation evidence before returning session details.
/// - This endpoint never creates or mutates PaymentAttempt, PaymentConfirmation, ExitAuthorization,
///   provider outcome, gate consume, coupon application, statutory discount validation, settlement truth,
///   reconciliation records, or payment finality.
/// </summary>
public static class OperatorConsoleSessionLookupEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleSessionLookup");

    /// <summary>
    /// Maps Operator Console session lookup endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleSessionLookupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapPost("/sessions/lookup", LookupAsync)
            .WithName("LookupOperatorConsoleSession")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleSessionLookupRequest>("application/json")
            .Produces<OperatorConsoleSessionLookupResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleSessionLookupResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Lookup Operator Console session")
            .WithDescription("Looks up read-only parking session context after evaluating and persisting Operator Console access. This endpoint does not mutate payment, gate, coupon, provider, statutory discount, settlement, or reconciliation state.");

        return app;
    }

    private static async Task<IResult> LookupAsync(
        OperatorConsoleSessionLookupRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleSessionLookupService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP LookupOperatorConsoleSession", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleSessionLookupEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("lookup_mode", request.LookupMode);
        activity?.SetTag("parking_session_id", request.ParkingSessionId);

        try
        {
            var result = await service.LookupAsync(
                new OperatorConsoleSessionLookupCommand(
                    request.UserId,
                    request.OperatorDeviceBindingId,
                    request.SiteId,
                    request.SiteGroupId,
                    request.OperatorShiftId,
                    request.ParkingSessionId,
                    request.TicketReference,
                    request.PlateNumber,
                    request.LookupMode,
                    request.IdempotencyKey,
                    request.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("session_found", result.Session is not null);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console session lookup completed. evaluation_id={EvaluationId} access_allowed={AccessAllowed} session_found={SessionFound} session_eligible={SessionEligible}",
                result.AccessEvaluationId,
                result.AccessAllowed,
                result.Session is not null,
                result.SessionEligible);

            var response = ToContract(result);
            return result.AccessAllowed && result.Session is null
                ? Results.NotFound(response)
                : Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_SESSION_LOOKUP_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console session lookup failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_SESSION_LOOKUP_FAILED",
                    "The Operator Console session lookup could not be completed.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };

    private static OperatorConsoleSessionLookupResponse ToContract(OperatorConsoleSessionLookupResult result) =>
        new(
            result.AccessEvaluationId,
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            result.Session is not null,
            result.SessionEligible,
            result.IneligibilityReason,
            result.Session?.ParkingSessionId,
            result.Session?.TicketReference,
            result.Session?.PlateNumber,
            result.Session?.SiteId,
            result.Session?.SiteGroupId,
            result.Session?.SessionStatus,
            result.Session?.EntryTime,
            result.Session?.CurrentPayableAmountMinorUnits,
            result.Session?.CurrencyCode,
            result.Session?.PaymentStatus,
            result.Session?.DiscountStatus,
            result.Session?.ExitAuthorizationStatus,
            result.Alerts,
            result.CorrelationId);
}
