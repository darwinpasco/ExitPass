using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Payments;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for recording verified payment confirmation evidence from providers.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - HTTP boundary requires correlation and idempotency headers before recording provider outcome
/// - Duplicate provider evidence must not create duplicate financial finality
/// </summary>
public static class InternalPaymentConfirmationEndpoints
{
    /// <summary>
    /// Activity source for internal payment confirmation endpoint spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api");

    /// <summary>
    /// Maps the internal endpoint for recording verified payment confirmation.
    /// </summary>
    /// <param name="app">Route builder used to register the endpoint.</param>
    /// <returns>The same route builder for fluent configuration.</returns>
    public static IEndpointRouteBuilder MapInternalPaymentConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/payments")
            .WithTags("InternalPayments");

        group.MapPost("/confirmation", HandleAsync)
            .WithName("RecordPaymentConfirmation")
            .Produces<RecordPaymentConfirmationResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// Handles recording of verified payment confirmation evidence.
    /// </summary>
    /// <param name="request">Incoming HTTP request.</param>
    /// <param name="body">Verified provider outcome payload.</param>
    /// <param name="service">Application service for recording payment confirmation.</param>
    /// <param name="loggerFactory">Logger factory used to create endpoint logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An HTTP result describing the outcome.</returns>
    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        RecordPaymentConfirmationRequest body,
        RecordPaymentConfirmationService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("InternalPaymentConfirmationEndpoint");

        using var activity = ActivitySource.StartActivity("HTTP RecordPaymentConfirmation", ActivityKind.Server);

        activity?.SetTag("http.route", "/v1/internal/payments/outcome");
        activity?.SetTag("payment_attempt_id", body.PaymentAttemptId);
        activity?.SetTag("provider_reference", body.ProviderReference);
        activity?.SetTag("provider_status", body.ProviderStatus);

        var startedAt = DateTimeOffset.UtcNow;

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

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId,
            ["payment_attempt_id"] = body.PaymentAttemptId,
            ["provider_reference"] = body.ProviderReference,
            ["provider_status"] = body.ProviderStatus
        });

        logger.LogInformation("HTTP RecordPaymentConfirmation request received.");

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

        try
        {
            var result = await service.ExecuteAsync(
                new RecordPaymentConfirmationCommand(
                    PaymentAttemptId: body.PaymentAttemptId,
                    ProviderReference: body.ProviderReference,
                    ProviderStatus: body.ProviderStatus,
                    RequestedBy: body.RequestedBy,
                    RawCallbackReference: body.RawCallbackReference,
                    ProviderSignatureValid: body.ProviderSignatureValid,
                    ProviderPayloadHash: body.ProviderPayloadHash,
                    AmountConfirmed: body.AmountConfirmed,
                    CurrencyCode: body.CurrencyCode,
                    CorrelationId: correlationId),
                cancellationToken);

            var duration = DateTimeOffset.UtcNow - startedAt;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("payment_confirmation_id", result.PaymentConfirmationId);
            activity?.SetTag("confirmation_status", result.ConfirmationStatus);
            activity?.SetTag("verified_timestamp", result.VerifiedTimestamp);
            activity?.SetTag("duration_ms", duration.TotalMilliseconds);

            logger.LogInformation(
                "HTTP RecordPaymentConfirmation succeeded. payment_confirmation_id={PaymentConfirmationId} confirmation_status={ConfirmationStatus}",
                result.PaymentConfirmationId,
                result.ConfirmationStatus);

            return Results.Created(
                $"/v1/internal/payments/outcome/{result.PaymentConfirmationId}",
                new RecordPaymentConfirmationResponse(
                    PaymentConfirmationId: result.PaymentConfirmationId,
                    PaymentAttemptId: result.PaymentAttemptId,
                    ProviderReference: result.ProviderReference,
                    ProviderStatus: result.ProviderStatus,
                    ConfirmationStatus: result.ConfirmationStatus,
                    VerifiedTimestamp: result.VerifiedTimestamp));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            logger.LogWarning(ex, "HTTP RecordPaymentConfirmation rejected because the request is invalid.");

            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: ex.Message,
                correlationId: correlationId));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);

            logger.LogWarning("HTTP RecordPaymentConfirmation rejected because payment attempt was not found.");

            return Results.NotFound(BuildError(
                errorCode: "PAYMENT_ATTEMPT_NOT_FOUND",
                message: "Payment attempt was not found.",
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["payment_attempt_id"] = body.PaymentAttemptId
                }));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);

            logger.LogWarning("HTTP RecordPaymentConfirmation rejected because provider reference was already recorded.");

            return Results.Conflict(BuildError(
                errorCode: "PAYMENT_CONFIRMATION_DUPLICATE_PROVIDER_REFERENCE",
                message: "Provider reference has already been recorded.",
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["payment_attempt_id"] = body.PaymentAttemptId,
                    ["provider_reference"] = body.ProviderReference
                }));
        }
    }

    /// <summary>
    /// Builds a standardized error response.
    /// </summary>
    /// <param name="errorCode">Application error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="correlationId">Correlation ID.</param>
    /// <param name="retryable">Whether retry is allowed.</param>
    /// <param name="details">Optional structured error details.</param>
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
