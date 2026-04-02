using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 10.6 Internal Service APIs
///
/// Invariants Enforced:
/// - Only Central PMS may issue ExitAuthorization
/// - HTTP boundary requires correlation and idempotency headers before issuance
/// - Issuance remains a deterministic, DB-backed control transition
/// </summary>
public static class InternalPaymentAttemptExitAuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapInternalPaymentAttemptExitAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/payment-attempts")
            .WithTags("InternalPaymentAttempts");

        group.MapPost("/{paymentAttemptId:guid}/issue-exit-authorization", HandleAsync)
            .WithName("IssueExitAuthorization")
            .Produces<IssueExitAuthorizationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid paymentAttemptId,
        HttpRequest request,
        IssueExitAuthorizationRequest body,
        IIssueExitAuthorizationUseCase useCase,
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
                new IssueExitAuthorizationCommand(
                    ParkingSessionId: body.ParkingSessionId,
                    PaymentAttemptId: paymentAttemptId,
                    RequestedByUserId: body.RequestedByUserId,
                    CorrelationId: correlationId),
                cancellationToken);

            return Results.Ok(
                new IssueExitAuthorizationResponse(
                    ExitAuthorizationId: result.ExitAuthorizationId,
                    ParkingSessionId: result.ParkingSessionId,
                    PaymentAttemptId: result.PaymentAttemptId,
                    AuthorizationToken: result.AuthorizationToken,
                    AuthorizationStatus: result.AuthorizationStatus,
                    IssuedAt: result.IssuedAt,
                    ExpirationTimestamp: result.ExpirationTimestamp));
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
                errorCode: "EXIT_AUTHORIZATION_ISSUANCE_CONFLICT",
                message: ex.MessageText,
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
                errorCode: "EXIT_AUTHORIZATION_ISSUANCE_FAILED",
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

    public sealed record IssueExitAuthorizationRequest(
        Guid ParkingSessionId,
        Guid RequestedByUserId);

    public sealed record IssueExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);
}
