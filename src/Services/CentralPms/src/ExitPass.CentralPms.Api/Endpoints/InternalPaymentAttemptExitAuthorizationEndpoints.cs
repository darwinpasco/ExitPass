using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal endpoints for issuing exit authorizations from confirmed payment attempts.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 10.6 Internal Service APIs
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - Only Central PMS may issue ExitAuthorization
/// - HTTP boundary requires correlation and idempotency headers before issuance
/// - Issuance remains a deterministic, DB-backed control transition
/// </summary>
public static class InternalPaymentAttemptExitAuthorizationEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api");

    /// <summary>
    /// Maps the internal endpoint for issuing exit authorizations from payment attempts.
    /// </summary>
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

    /// <summary>
    /// Handles issuance of exit authorization from a confirmed payment attempt.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid paymentAttemptId,
        HttpRequest request,
        IssueExitAuthorizationRequest body,
        IIssueExitAuthorizationUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ExitAuthorizationEndpoint");

        using var activity = ActivitySource.StartActivity("HTTP IssueExitAuthorization", ActivityKind.Server);

        activity?.SetTag("http.route", "/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization");
        activity?.SetTag("payment_attempt_id", paymentAttemptId);

        var start = DateTimeOffset.UtcNow;

        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "X-Correlation-Id header is required.", Guid.Empty));
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId,
            ["payment_attempt_id"] = paymentAttemptId
        });

        activity?.SetTag("correlation_id", correlationId);

        logger.LogInformation("HTTP IssueExitAuthorization request received.");

        if (!request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader))
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "Idempotency-Key header is required.", correlationId));
        }

        if (paymentAttemptId == Guid.Empty)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "paymentAttemptId is required.", correlationId));
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                new IssueExitAuthorizationCommand(
                    body.ParkingSessionId,
                    paymentAttemptId,
                    body.RequestedByUserId,
                    correlationId),
                cancellationToken);

            var duration = DateTimeOffset.UtcNow - start;

            activity?.SetTag("exit_authorization_id", result.ExitAuthorizationId);
            activity?.SetTag("duration_ms", duration.TotalMilliseconds);

            logger.LogInformation(
                "HTTP IssueExitAuthorization succeeded. exit_authorization_id={ExitAuthorizationId}",
                result.ExitAuthorizationId);

            return Results.Ok(new IssueExitAuthorizationResponse(
                result.ExitAuthorizationId,
                result.ParkingSessionId,
                result.PaymentAttemptId,
                result.AuthorizationToken,
                result.AuthorizationStatus,
                result.IssuedAt,
                result.ExpirationTimestamp));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid request.");
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            logger.LogWarning("Payment attempt not found.");
            return Results.NotFound(BuildError("PAYMENT_ATTEMPT_NOT_FOUND", "Payment attempt was not found.", correlationId));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001")
        {
            logger.LogWarning("Issuance conflict.");
            return Results.Conflict(BuildError("EXIT_AUTHORIZATION_ISSUANCE_CONFLICT", ex.MessageText, correlationId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure.");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_ISSUANCE_FAILED",
                "Unexpected error during issuance.",
                correlationId,
                retryable: true));
        }
    }

    /// <summary>
    /// Builds a standardized error response.
    /// </summary>
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

    /// <summary>
    /// HTTP request body for issuing exit authorization.
    /// </summary>
    public sealed record IssueExitAuthorizationRequest(Guid ParkingSessionId, Guid RequestedByUserId);

    /// <summary>
    /// HTTP response for issued exit authorization.
    /// </summary>
    public sealed record IssueExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);
}
