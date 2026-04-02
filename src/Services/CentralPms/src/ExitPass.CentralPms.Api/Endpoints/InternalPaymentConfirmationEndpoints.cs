using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Payments;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - HTTP boundary requires correlation and idempotency headers before recording provider outcome
/// - Duplicate provider evidence must not create duplicate financial finality
/// </summary>
public static class InternalPaymentConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapInternalPaymentConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/payments")
            .WithTags("InternalPayments");

        group.MapPost("/outcome", HandleAsync)
            .WithName("RecordPaymentConfirmation")
            .Produces<RecordPaymentConfirmationResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        RecordPaymentConfirmationRequest body,
        RecordPaymentConfirmationService service,
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
                details: new Dictionary<string, object?>()));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return Results.Conflict(BuildError(
                errorCode: "PAYMENT_CONFIRMATION_DUPLICATE_PROVIDER_REFERENCE",
                message: "Provider reference has already been recorded.",
                correlationId: correlationId,
                details: new Dictionary<string, object?>()));
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
