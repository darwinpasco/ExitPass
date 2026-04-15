using System.Diagnostics;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Internal;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for payment outcome handling.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 9.21 Audit and Traceability
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - Only authenticated internal callers may report verified payment outcomes.
/// - Required trace headers are enforced for side-effecting internal operations.
/// - Business conflicts must be distinguished from unexpected server failures.
/// - Read endpoints must not mutate state.
/// </summary>
public static class InternalPaymentOutcomeEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api");

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
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

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
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP result representing the workflow outcome.</returns>
    private static async Task<IResult> HandleReportVerifiedOutcomeAsync(
        ReportVerifiedPaymentOutcomeRequest request,
        HttpRequest httpRequest,
        IReportVerifiedPaymentOutcomeUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpRequest);
        ArgumentNullException.ThrowIfNull(useCase);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.InternalPaymentOutcomeEndpoints");

        using var activity = ActivitySource.StartActivity("HTTP ReportVerifiedPaymentOutcome", ActivityKind.Server);

        if (!httpRequest.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "X-Correlation-Id header is required.");
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "CORRELATION_ID_REQUIRED");

            return Results.BadRequest(BuildError(
                "CORRELATION_ID_REQUIRED",
                "X-Correlation-Id header is required.",
                Guid.Empty,
                retryable: false));
        }

        if (!httpRequest.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader.ToString()))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Idempotency-Key header is required.");
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "IDEMPOTENCY_KEY_REQUIRED");
            activity?.SetTag("correlation_id", correlationId);

            return Results.BadRequest(BuildError(
                "IDEMPOTENCY_KEY_REQUIRED",
                "Idempotency-Key header is required.",
                correlationId,
                retryable: false));
        }

        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("payment_attempt_id", request.PaymentAttemptId);
        activity?.SetTag("parking_session_id", request.ParkingSessionId);
        activity?.SetTag("provider_reference", request.ProviderReference);
        activity?.SetTag("final_attempt_status", request.FinalAttemptStatus);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId,
            ["payment_attempt_id"] = request.PaymentAttemptId,
            ["parking_session_id"] = request.ParkingSessionId,
            ["provider_reference"] = request.ProviderReference,
            ["final_attempt_status"] = request.FinalAttemptStatus
        });

        logger.LogInformation("Internal payment outcome report received.");

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

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("attempt_status", result.AttemptStatus);
            activity?.SetTag("exit_authorization_id", result.ExitAuthorizationId);
            activity?.SetTag("authorization_status", result.AuthorizationStatus);

            logger.LogInformation(
                "Internal payment outcome report succeeded. payment_attempt_id={PaymentAttemptId} attempt_status={AttemptStatus}",
                result.PaymentAttemptId,
                result.AttemptStatus);

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
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "INVALID_REQUEST");

            logger.LogWarning(ex, "Internal payment outcome report rejected due to invalid request.");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (KeyNotFoundException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "PAYMENT_ATTEMPT_NOT_FOUND");

            logger.LogWarning(ex, "Internal payment outcome report rejected because the payment attempt was not found.");

            return Results.NotFound(BuildError(
                "PAYMENT_ATTEMPT_NOT_FOUND",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (DuplicatePaymentConfirmationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "PROVIDER_REFERENCE_ALREADY_RECORDED");

            logger.LogWarning(ex, "Internal payment outcome report rejected due to duplicate provider reference.");

            return Results.Conflict(BuildError(
                "PROVIDER_REFERENCE_ALREADY_RECORDED",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (PaymentConfirmationConflictException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", ex.ErrorCode);

            logger.LogWarning(ex, "Internal payment outcome report rejected by deterministic business conflict.");

            return Results.Conflict(BuildError(
                ex.ErrorCode,
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "PAYMENT_ATTEMPT_ALREADY_FINAL");

            logger.LogWarning(ex, "Internal payment outcome report rejected because the payment attempt is already final.");

            return Results.Conflict(BuildError(
                "PAYMENT_ATTEMPT_ALREADY_FINAL",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "SYSTEM_FAILURE");
            activity?.SetTag("error_code", "INTERNAL_PAYMENT_OUTCOME_REPORT_FAILED");

            logger.LogError(ex, "Unexpected failure while reporting verified payment outcome.");

            return Results.Json(
                BuildError(
                    "INTERNAL_PAYMENT_OUTCOME_REPORT_FAILED",
                    "An unexpected error occurred while processing the verified payment outcome.",
                    correlationId,
                    retryable: false),
                statusCode: StatusCodes.Status500InternalServerError);
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

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable,
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
