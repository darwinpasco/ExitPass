using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Gate-facing endpoints for consuming exit authorizations.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4.2 Consume Exit Authorization
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - A valid authorization may be consumed only once.
/// - Business conflicts must be distinguished from unexpected server failures.
/// - Trace metadata must be preserved at the HTTP boundary.
/// </summary>
public static class GateExitAuthorizationConsumeEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api");

    /// <summary>
    /// Maps gate-facing exit authorization consume endpoints.
    /// </summary>
    /// <param name="app">Route builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IEndpointRouteBuilder MapGateExitAuthorizationConsumeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/gate/authorizations")
            .WithTags("GateAuthorizations");

        group.MapPost("/{exitAuthorizationId:guid}/consume", HandleAsync)
            .WithName("ConsumeExitAuthorization")
            .Produces<ConsumeExitAuthorizationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Consumes a previously issued exit authorization.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid exitAuthorizationId,
        HttpRequest request,
        ConsumeExitAuthorizationRequest body,
        IConsumeExitAuthorizationUseCase useCase,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("GateConsumeEndpoint");

        using var activity = ActivitySource.StartActivity("HTTP ConsumeExitAuthorization", ActivityKind.Server);

        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "X-Correlation-Id header is required.");
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "INVALID_REQUEST");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "X-Correlation-Id header is required.",
                Guid.Empty,
                retryable: false));
        }

        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("exit_authorization_id", exitAuthorizationId);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId,
            ["exit_authorization_id"] = exitAuthorizationId
        });

        logger.LogInformation("HTTP ConsumeExitAuthorization request received.");

        try
        {
            var result = await useCase.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    exitAuthorizationId,
                    body.RequestedByUserId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("authorization_status", result.AuthorizationStatus);
            activity?.SetTag("consumed_at", result.ConsumedAt);

            logger.LogInformation(
                "Exit authorization consumed. exit_authorization_id={ExitAuthorizationId}",
                result.ExitAuthorizationId);

            return Results.Ok(new ConsumeExitAuthorizationResponse(
                result.ExitAuthorizationId,
                result.AuthorizationStatus,
                result.ConsumedAt));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "INVALID_REQUEST");

            logger.LogWarning(ex, "Invalid consume request.");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_NOT_FOUND");

            logger.LogWarning(ex, "Exit authorization not found.");

            return Results.NotFound(BuildError(
                "EXIT_AUTHORIZATION_NOT_FOUND",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "P0001" &&
            ex.MessageText.Contains("is expired", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_EXPIRED");

            logger.LogWarning(ex, "Exit authorization expired.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_EXPIRED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (
            ex.SqlState == "P0001" &&
            ex.MessageText.Contains("already been consumed", StringComparison.OrdinalIgnoreCase))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.MessageText);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_ALREADY_CONSUMED");

            logger.LogWarning(ex, "Exit authorization already consumed.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "BUSINESS_REJECTION");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_REJECTED");

            logger.LogWarning(ex, "Consume rejected by deterministic business rule.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_REJECTED",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("failure_class", "SYSTEM_FAILURE");
            activity?.SetTag("error_code", "EXIT_AUTHORIZATION_CONSUME_INTERNAL_ERROR");

            logger.LogError(ex, "Unexpected failure.");

            return Results.Json(
                BuildError(
                    "EXIT_AUTHORIZATION_CONSUME_INTERNAL_ERROR",
                    "An unexpected error occurred while consuming the exit authorization.",
                    correlationId,
                    retryable: false),
                 statusCode: StatusCodes.Status500InternalServerError);
        }
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

    /// <summary>
    /// Consume request body.
    /// </summary>
    public sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

    /// <summary>
    /// Consume response body.
    /// </summary>
    public sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
