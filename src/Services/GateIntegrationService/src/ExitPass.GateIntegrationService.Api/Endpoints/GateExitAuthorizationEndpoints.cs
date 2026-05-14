using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Contracts.GateExit;

namespace ExitPass.GateIntegrationService.Api.Endpoints;

/// <summary>
/// Gate-device endpoint for consuming exit authorizations and opening the barrier.
/// </summary>
public static class GateExitAuthorizationEndpoints
{
    /// <summary>
    /// Maps gate-facing exit authorization consume endpoints for the v1.2 Central PMS authority model.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The endpoint route builder with gate authorization endpoints registered.</returns>
    public static IEndpointRouteBuilder MapGateExitAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/gate/authorizations")
            .WithTags("GateAuthorizations");

        group.MapPost("/{exitAuthorizationId:guid}/consume", HandleAsync)
            .WithName("GateConsumeExitAuthorization")
            .Produces<ConsumeGateExitAuthorizationResponse>(StatusCodes.Status200OK)
            .Produces<ConsumeGateExitAuthorizationResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ConsumeGateExitAuthorizationResponse>(StatusCodes.Status404NotFound)
            .Produces<ConsumeGateExitAuthorizationResponse>(StatusCodes.Status409Conflict)
            .Produces<ConsumeGateExitAuthorizationResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid exitAuthorizationId,
        HttpRequest request,
        IConsumeGateExitAuthorizationUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) ||
            !Guid.TryParse(correlationHeader.ToString(), out var correlationId))
        {
            return Results.BadRequest(CreateDeniedResponse(
                exitAuthorizationId,
                "INVALID_REQUEST",
                "X-Correlation-Id header is required."));
        }

        if (!request.Headers.TryGetValue("X-Gate-Device-Id", out var gateDeviceHeader) ||
            string.IsNullOrWhiteSpace(gateDeviceHeader.ToString()))
        {
            return Results.Unauthorized();
        }

        if (!request.Headers.TryGetValue("X-Service-Identity-Id", out var serviceIdentityHeader) ||
            !Guid.TryParse(serviceIdentityHeader.ToString(), out var serviceIdentityId) ||
            serviceIdentityId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                new ConsumeGateExitAuthorizationCommand(
                    exitAuthorizationId,
                    gateDeviceHeader.ToString(),
                    serviceIdentityId,
                    correlationId),
                cancellationToken);

            var response = new ConsumeGateExitAuthorizationResponse(
                result.GateOpened,
                result.ResultCode,
                result.AuthorizationStatus,
                result.ExitAuthorizationId,
                result.ConsumedAt);

            return result.ResultCode switch
            {
                "GATE_OPENED" => Results.Ok(response),
                "EXIT_AUTHORIZATION_NOT_FOUND" => Results.NotFound(response),
                "EXIT_AUTHORIZATION_ALREADY_CONSUMED" => Results.Conflict(response),
                "EXIT_AUTHORIZATION_EXPIRED" => Results.Conflict(response),
                "CENTRAL_PMS_UNAVAILABLE" => Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Conflict(response)
            };
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(CreateDeniedResponse(exitAuthorizationId, "INVALID_REQUEST", ex.Message));
        }
    }

    private static ConsumeGateExitAuthorizationResponse CreateDeniedResponse(
        Guid exitAuthorizationId,
        string resultCode,
        string authorizationStatus)
    {
        return new ConsumeGateExitAuthorizationResponse(
            GateOpened: false,
            ResultCode: resultCode,
            AuthorizationStatus: authorizationStatus,
            ExitAuthorizationId: exitAuthorizationId,
            ConsumedAt: null);
    }
}
