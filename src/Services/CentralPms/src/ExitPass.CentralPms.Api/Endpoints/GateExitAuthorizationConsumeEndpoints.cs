using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Gate-facing endpoints for consuming exit authorizations.
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
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// Consumes a previously issued exit authorization.
    /// </summary>
    /// <param name="exitAuthorizationId">Exit authorization identifier.</param>
    /// <param name="request">Incoming HTTP request.</param>
    /// <param name="body">Consume request body.</param>
    /// <param name="useCase">Consume use case.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP result.</returns>
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
            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                "X-Correlation-Id header is required.",
                Guid.Empty,
                retryable: false));
        }

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
            logger.LogWarning(ex, "Invalid consume request.");

            return Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            logger.LogWarning(ex, "Exit authorization not found.");

            return Results.NotFound(BuildError(
                "EXIT_AUTHORIZATION_NOT_FOUND",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001" &&
                                                  ex.MessageText.Contains("is expired", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Exit authorization expired.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_EXPIRED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001" &&
                                                  ex.MessageText.Contains("already been consumed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Exit authorization already consumed.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
                ex.MessageText,
                correlationId,
                retryable: false));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Consume rejected by deterministic business rule.");

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_REJECTED",
                ex.Message,
                correlationId,
                retryable: false));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure.");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_FAILED",
                ex.Message,
                correlationId,
                retryable: false));
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
    /// <param name="RequestedByUserId">Actor requesting consumption.</param>
    public sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

    /// <summary>
    /// Consume response body.
    /// </summary>
    /// <param name="ExitAuthorizationId">Authorization identifier.</param>
    /// <param name="AuthorizationStatus">Final authorization status.</param>
    /// <param name="ConsumedAt">Consume timestamp.</param>
    public sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
