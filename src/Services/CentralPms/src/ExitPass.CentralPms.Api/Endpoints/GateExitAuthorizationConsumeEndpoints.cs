using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Gate-facing endpoints for consuming issued exit authorizations.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4 Gate / Site Integration APIs
///
/// Invariants Enforced:
/// - ExitAuthorization consume is the hard control point before physical exit
/// - Only an existing issued authorization may be consumed
/// - Consumption must remain a deterministic DB-backed control transition
/// </summary>
public static class GateExitAuthorizationConsumeEndpoints
{
    /// <summary>
    /// Maps the gate-facing consume endpoint for exit authorizations.
    /// </summary>
    /// <param name="app">Route builder used to register the endpoint.</param>
    /// <returns>The same route builder for fluent configuration.</returns>
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

    private static async Task<IResult> HandleAsync(
        Guid exitAuthorizationId,
        HttpRequest request,
        ConsumeExitAuthorizationRequest body,
        IConsumeExitAuthorizationUseCase useCase,
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

        if (exitAuthorizationId == Guid.Empty)
        {
            return Results.BadRequest(BuildError(
                errorCode: "INVALID_REQUEST",
                message: "exitAuthorizationId is required.",
                correlationId: correlationId));
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: exitAuthorizationId,
                    RequestedByUserId: body.RequestedByUserId,
                    CorrelationId: correlationId),
                cancellationToken);

            return Results.Ok(
                new ConsumeExitAuthorizationResponse(
                    ExitAuthorizationId: result.ExitAuthorizationId,
                    AuthorizationStatus: result.AuthorizationStatus,
                    ConsumedAt: result.ConsumedAt));
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
                errorCode: "EXIT_AUTHORIZATION_NOT_FOUND",
                message: "Exit authorization was not found.",
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["exit_authorization_id"] = exitAuthorizationId
                }));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0001")
        {
            return Results.Conflict(BuildError(
                errorCode: "EXIT_AUTHORIZATION_CONSUME_CONFLICT",
                message: ex.MessageText,
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["exit_authorization_id"] = exitAuthorizationId
                }));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(BuildError(
                errorCode: "EXIT_AUTHORIZATION_CONSUME_FAILED",
                message: ex.Message,
                correlationId: correlationId,
                details: new Dictionary<string, object?>
                {
                    ["exit_authorization_id"] = exitAuthorizationId
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

    /// <summary>
    /// HTTP request body for consuming an exit authorization.
    /// </summary>
    /// <param name="RequestedByUserId">User or actor identifier requesting consume.</param>
    public sealed record ConsumeExitAuthorizationRequest(
        Guid RequestedByUserId);

    /// <summary>
    /// HTTP response returned after an exit authorization is successfully consumed.
    /// </summary>
    /// <param name="ExitAuthorizationId">Canonical identifier of the consumed authorization.</param>
    /// <param name="AuthorizationStatus">Authorization status after consumption.</param>
    /// <param name="ConsumedAt">Timestamp at which the authorization was consumed.</param>
    public sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
