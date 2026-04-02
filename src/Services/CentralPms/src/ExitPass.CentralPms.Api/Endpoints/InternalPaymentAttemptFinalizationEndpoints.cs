using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Payments;
using Npgsql;

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
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - HTTP boundary requires correlation and idempotency headers before finalization
/// - Finalization must remain a deterministic, DB-backed control transition
/// </summary>
public static class InternalPaymentAttemptFinalizationEndpoints
{
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

    private static async Task<IResult> HandleAsync(
        Guid paymentAttemptId,
        HttpRequest request,
        FinalizePaymentAttemptRequest body,
        IFinalizePaymentAttemptUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: "X-Correlation-Id header is required.",
                correlationId: Guid.Empty));
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader.ToString()))
        {
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: "Idempotency-Key header is required.",
                correlationId: correlationId));
        }

        if (paymentAttemptId == Guid.Empty)
        {
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

            return Results.Ok(
                new FinalizePaymentAttemptResponse(
                    PaymentAttemptId: result.PaymentAttemptId,
                    AttemptStatus: result.AttemptStatus));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: ex.Message,
                correlationId: correlationId));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
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
