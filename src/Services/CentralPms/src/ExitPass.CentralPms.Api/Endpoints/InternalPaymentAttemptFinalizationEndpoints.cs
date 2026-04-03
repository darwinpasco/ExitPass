using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Payments;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for finalizing payment attempts after verified provider outcome.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 14.3 Distributed Tracing
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - HTTP boundary requires correlation and idempotency headers before finalization
/// - Finalization must remain a deterministic, DB-backed control transition
/// </summary>
public static class InternalPaymentAttemptFinalizationEndpoints
{
    /// <summary>
    /// Activity source for internal payment attempt finalization endpoints.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.InternalPaymentAttempts");

    /// <summary>
    /// Maps the internal endpoint for finalizing payment attempts.
    /// </summary>
    /// <param name="app">Route builder used to register the endpoint.</param>
    /// <returns>The same route builder for fluent configuration.</returns>
    public static IEndpointRouteBuilder MapInternalPaymentAttemptFinalizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/payment-attempts")
            .WithTags("InternalPaymentAttempts");

        group.MapPost("/{paymentAttemptId:guid}/finalize", HandleAsync)
            .WithName("FinalizePaymentAttempt")
            .Produces<FinalizePaymentAttemptResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// Handles internal payment attempt finalization requests.
    /// </summary>
    /// <param name="paymentAttemptId">Payment attempt ID from the route.</param>
    /// <param name="request">Incoming HTTP request.</param>
    /// <param name="body">Finalize payment attempt request body.</param>
    /// <param name="useCase">Finalize payment attempt use case.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An HTTP result for the finalization outcome.</returns>
    private static async Task<IResult> HandleAsync(
        Guid paymentAttemptId,
        HttpRequest request,
        FinalizePaymentAttemptRequest body,
        IFinalizePaymentAttemptUseCase useCase,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("FinalizePaymentAttempt", ActivityKind.Server);

        activity?.SetTag("payment_attempt_id", paymentAttemptId);
        activity?.SetTag("final_attempt_status", body?.FinalAttemptStatus);

        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing X-Correlation-Id header");
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: "X-Correlation-Id header is required.",
                correlationId: Guid.Empty));
        }

        activity?.SetTag("correlation_id", correlationId);

        if (!request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader.ToString()))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Idempotency-Key header");
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: "Idempotency-Key header is required.",
                correlationId: correlationId));
        }

        activity?.SetTag("idempotency_key", idempotencyHeader.ToString());

        if (paymentAttemptId == Guid.Empty)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "paymentAttemptId is required");
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: "paymentAttemptId is required.",
                correlationId: correlationId));
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                new FinalizePaymentAttemptCommand(
                    PaymentAttemptId: paymentAttemptId,
                    FinalAttemptStatus: body.FinalAttemptStatus,
                    RequestedBy: body.RequestedBy,
                    CorrelationId: correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("attempt_status", result.AttemptStatus);

            return Results.Ok(
                new FinalizePaymentAttemptResponse(
                    PaymentAttemptId: result.PaymentAttemptId,
                    AttemptStatus: result.AttemptStatus));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: ex.Message,
                correlationId: correlationId));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);

            return Results.NotFound(BuildError(
                errorCode: "PAYMENT_ATTEMPT_NOT_FOUND",
                message: "Payment attempt was not found.",
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["payment_attempt_id"] = paymentAttemptId
                }));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);

            return Results.Conflict(BuildError(
                errorCode: "PAYMENT_ATTEMPT_ALREADY_FINAL",
                message: "Payment attempt is already in a terminal state.",
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["payment_attempt_id"] = paymentAttemptId
                }));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "22023")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);

            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: ex.MessageText,
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["payment_attempt_id"] = paymentAttemptId
                }));
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            return Results.Conflict(BuildError(
                errorCode: "PAYMENT_ATTEMPT_FINALIZATION_CONFLICT",
                message: ex.Message,
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["payment_attempt_id"] = paymentAttemptId
                }));
        }
    }

    /// <summary>
    /// Builds a standardized error response for internal payment attempt finalization endpoints.
    /// </summary>
    /// <param name="errorCode">Application error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="correlationId">Correlation ID.</param>
    /// <param name="retryable">Whether the client may retry.</param>
    /// <param name="details">Optional error details.</param>
    /// <returns>A standardized error response.</returns>
    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable = false,
        Dictionary<string, object?>? details = null)
    {
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable,
            Details = details
        };
    }
}
