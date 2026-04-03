using System.Diagnostics;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Gate-facing endpoints for consuming exit authorizations.
/// </summary>
public static class GateExitAuthorizationConsumeEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api");

    /// <summary>
    /// Maps the gate-facing consume endpoint.
    /// </summary>
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
    /// Handles consumption of exit authorization.
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

        var start = DateTimeOffset.UtcNow;

        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "X-Correlation-Id header is required.", Guid.Empty));
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = correlationId,
            ["exit_authorization_id"] = exitAuthorizationId
        });

        logger.LogInformation("HTTP ConsumeExitAuthorization request received.");

        if (exitAuthorizationId == Guid.Empty)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "exitAuthorizationId is required.", correlationId));
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    exitAuthorizationId,
                    body.RequestedByUserId,
                    correlationId),
                cancellationToken);

            var duration = DateTimeOffset.UtcNow - start;

            activity?.SetTag("duration_ms", duration.TotalMilliseconds);

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
            logger.LogWarning(ex, "Invalid request.");
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            return Results.NotFound(BuildError("EXIT_AUTHORIZATION_NOT_FOUND", "Not found.", correlationId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure.");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            return Results.Conflict(BuildError(
                "EXIT_AUTHORIZATION_CONSUME_FAILED",
                "Unexpected error during consume.",
                correlationId,
                retryable: true));
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

    public sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

    public sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
