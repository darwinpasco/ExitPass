using System.Diagnostics;
using ExitPass.CentralPms.Application.Operations;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Operations;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Ops-facing ticket session summary endpoint.
/// </summary>
public static class TicketSessionSummaryEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.TicketSessionSummary");

    /// <summary>
    /// Maps ticket session summary endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapTicketSessionSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops")
            .WithTags("Operations");

        group.MapPost("/ticket-session-summary", GetAsync)
            .WithName("GetTicketSessionSummary")
            .Accepts<TicketSessionSummaryRequest>("application/json")
            .Produces<TicketSessionSummaryResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get ticket session summary")
            .WithDescription("Retrieves a read-only ticket/session/tariff/payment/vendor status summary. This endpoint does not confirm parking fees, mark payment paid, issue exit authorization, or open gates.");

        return app;
    }

    private static async Task<IResult> GetAsync(
        TicketSessionSummaryRequest request,
        HttpRequest httpRequest,
        ITicketSessionSummaryService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetTicketSessionSummary", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.TicketSessionSummaryEndpoints");

        var correlationId = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId;
        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("site_id", request.SiteId);
        activity?.SetTag("site_group_id", request.SiteGroupId);
        httpRequest.HttpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();

        var result = await service.GetAsync(
            new TicketSessionSummaryCommand(
                request.TicketNumber,
                request.CardNum,
                request.SiteId,
                request.SiteGroupId,
                correlationId),
            httpRequest.HttpContext.RequestAborted);

        activity?.SetTag("ticket_summary.outcome", result.Outcome.ToString());
        activity?.SetTag("ticket_summary.error_code", result.ErrorCode);
        activity?.SetTag("ticket_summary.retryable", result.Retryable);
        activity?.SetStatus(result.Outcome == TicketSessionSummaryOutcome.Resolved ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

        if (result.Outcome == TicketSessionSummaryOutcome.Resolved)
        {
            logger.LogInformation(
                "Ticket session summary resolved. correlation_id={CorrelationId} parking_session_id={ParkingSessionId} payment_attempt_id={PaymentAttemptId}",
                result.CorrelationId,
                result.Summary?.ParkingSessionId,
                result.Summary?.PaymentAttemptId);

            return Results.Ok(ToContract(result.Summary!, result.Diagnostics, result.CorrelationId));
        }

        logger.LogWarning(
            "Ticket session summary failed. correlation_id={CorrelationId} outcome={Outcome} error_code={ErrorCode} retryable={Retryable}",
            result.CorrelationId,
            result.Outcome,
            result.ErrorCode,
            result.Retryable);

        var error = BuildError(result);
        return result.Outcome switch
        {
            TicketSessionSummaryOutcome.InvalidRequest => Results.BadRequest(error),
            TicketSessionSummaryOutcome.NotFound => Results.NotFound(error),
            TicketSessionSummaryOutcome.Ambiguous => Results.Conflict(error),
            TicketSessionSummaryOutcome.AdapterUnavailable => Results.Json(error, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(error, statusCode: StatusCodes.Status502BadGateway)
        };
    }

    private static TicketSessionSummaryResponse ToContract(
        TicketSessionSummaryReadModel summary,
        IReadOnlyList<TicketSessionSummaryDiagnostic> diagnostics,
        Guid correlationId) =>
        new()
        {
            TicketNumber = summary.TicketNumber,
            CardNum = summary.CardNum,
            PlateLicense = summary.PlateLicense,
            ParkingInTime = summary.ParkingInTime,
            ParkingDurationSeconds = summary.ParkingDurationSeconds,
            FeeMinorUnits = summary.FeeMinorUnits,
            CurrencyCode = summary.CurrencyCode,
            FeeRuleType = summary.FeeRuleType,
            FeeRuleIndexCode = summary.FeeRuleIndexCode,
            FeeRuleName = summary.FeeRuleName,
            VendorSessionStatus = summary.VendorSessionStatus,
            VendorSystemCode = summary.VendorSystemCode,
            VendorConfirmationCode = summary.VendorConfirmationCode,
            VendorMessage = summary.VendorMessage,
            ParkingSessionId = summary.ParkingSessionId,
            PaymentAttemptId = summary.PaymentAttemptId,
            PaymentAttemptStatus = summary.PaymentAttemptStatus,
            PaymentStatus = summary.PaymentStatus,
            PaymentConfirmationStatus = summary.PaymentConfirmationStatus,
            VendorConfirmationStatus = summary.VendorConfirmationStatus,
            VendorConfirmationTimestamp = summary.VendorConfirmationTimestamp,
            Diagnostics = diagnostics.Select(ToContract).ToArray(),
            CorrelationId = correlationId
        };

    private static TicketSessionSummaryDiagnosticDto ToContract(TicketSessionSummaryDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Source,
            diagnostic.Retryable,
            diagnostic.VendorSystemCode,
            diagnostic.VendorConfirmationCode,
            diagnostic.VendorMessage,
            diagnostic.CorrelationId);

    private static ErrorResponse BuildError(TicketSessionSummaryResult result) =>
        new()
        {
            ErrorCode = result.ErrorCode ?? "TICKET_SESSION_SUMMARY_FAILED",
            Message = ResolveMessage(result.Outcome),
            CorrelationId = result.CorrelationId,
            Retryable = result.Retryable,
            Details = new Dictionary<string, object?>
            {
                ["diagnostics"] = result.Diagnostics.Select(ToContract).ToArray()
            }
        };

    private static string ResolveMessage(TicketSessionSummaryOutcome outcome)
    {
        return outcome switch
        {
            TicketSessionSummaryOutcome.InvalidRequest => "The ticket session summary request is invalid.",
            TicketSessionSummaryOutcome.NotFound => "Ticket session was not found.",
            TicketSessionSummaryOutcome.Ambiguous => "Ticket session lookup was ambiguous.",
            TicketSessionSummaryOutcome.AdapterUnavailable => "Ticket session summary adapter is unavailable.",
            _ => "Ticket session summary could not be completed."
        };
    }
}
