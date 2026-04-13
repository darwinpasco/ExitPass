using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Internal;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for payment outcome handling.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.21 Audit and Traceability
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
/// - 10 Reporting / Query APIs
///
/// Invariants Enforced:
/// - Only authenticated internal callers may report verified payment outcomes
/// - Required trace headers are enforced for side-effecting internal operations
/// - Read endpoints must not mutate state
/// </summary>
public static class InternalPaymentOutcomeEndpoints
{
    /// <summary>
    /// Maps the internal payment outcome endpoints.
    /// </summary>
    /// <param name="app">Route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapInternalPaymentOutcomeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/payments")
            .WithTags("InternalPayments");

        group.MapPost("/outcome", HandleReportVerifiedOutcomeAsync)
            .WithName("ReportVerifiedPaymentOutcome")
            .Produces<ReportVerifiedPaymentOutcomeResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/outcome/{paymentAttemptId:guid}", HandleReadAsync)
            .WithName("GetPaymentOutcome")
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Handles the verified payment outcome report from the Payment Orchestrator.
    /// </summary>
    /// <param name="request">Verified outcome request body.</param>
    /// <param name="httpRequest">Incoming HTTP request.</param>
    /// <param name="useCase">Application use case.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP result representing the workflow outcome.</returns>
    private static async Task<IResult> HandleReportVerifiedOutcomeAsync(
        ReportVerifiedPaymentOutcomeRequest request,
        HttpRequest httpRequest,
        IReportVerifiedPaymentOutcomeUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            return Results.BadRequest(new ErrorResponse
            {
                ErrorCode = "CORRELATION_ID_REQUIRED",
                Message = "X-Correlation-Id header is required.",
                CorrelationId = Guid.Empty,
                Retryable = false,
                Details = null
            });
        }

        if (!httpRequest.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader.ToString()))
        {
            return Results.BadRequest(new ErrorResponse
            {
                ErrorCode = "IDEMPOTENCY_KEY_REQUIRED",
                Message = "Idempotency-Key header is required.",
                CorrelationId = correlationId,
                Retryable = false,
                Details = null
            });
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                new ReportVerifiedPaymentOutcomeCommand(
                    request.PaymentAttemptId,
                    request.ParkingSessionId,
                    request.ProviderReference,
                    request.ProviderStatus,
                    request.FinalAttemptStatus,
                    request.RequestedBy,
                    request.RequestedByUserId,
                    correlationId),
                cancellationToken);

            return Results.Ok(new ReportVerifiedPaymentOutcomeResponse(
                PaymentConfirmationId: result.PaymentConfirmationId,
                PaymentAttemptId: result.PaymentAttemptId,
                AttemptStatus: result.AttemptStatus,
                ExitAuthorizationId: result.ExitAuthorizationId,
                AuthorizationToken: result.AuthorizationToken,
                AuthorizationStatus: result.AuthorizationStatus,
                VerifiedTimestamp: result.VerifiedTimestamp,
                IssuedAt: result.IssuedAt,
                ExpirationTimestamp: result.ExpirationTimestamp));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse
            {
                ErrorCode = "INVALID_REQUEST",
                Message = ex.Message,
                CorrelationId = correlationId,
                Retryable = false,
                Details = null
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new ErrorResponse
            {
                ErrorCode = "PAYMENT_ATTEMPT_NOT_FOUND",
                Message = ex.Message,
                CorrelationId = correlationId,
                Retryable = false,
                Details = null
            });
        }
        catch (DuplicatePaymentConfirmationException ex)
        {
            return Results.Conflict(new ErrorResponse
            {
                ErrorCode = "PROVIDER_REFERENCE_ALREADY_RECORDED",
                Message = ex.Message,
                CorrelationId = correlationId,
                Retryable = false,
                Details = null
            });
        }
        catch (PaymentConfirmationConflictException ex)
        {
            return Results.Conflict(new ErrorResponse
            {
                ErrorCode = ex.ErrorCode,
                Message = ex.Message,
                CorrelationId = correlationId,
                Retryable = false,
                Details = null
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse
            {
                ErrorCode = "PAYMENT_ATTEMPT_ALREADY_FINAL",
                Message = ex.Message,
                CorrelationId = correlationId,
                Retryable = false,
                Details = null
            });
        }
        
    }

    /// <summary>
    /// Reads the current payment outcome state for the supplied payment attempt identifier.
    /// </summary>
    /// <param name="paymentAttemptId">Payment attempt identifier.</param>
    /// <returns>Placeholder read response until the read model is implemented.</returns>
    private static IResult HandleReadAsync(Guid paymentAttemptId)
    {
        return Results.Ok(new
        {
            PaymentAttemptId = paymentAttemptId,
            Status = "NOT_IMPLEMENTED"
        });
    }
}
