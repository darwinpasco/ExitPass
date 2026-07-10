using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal fiscal issuance command endpoints.
/// </summary>
public static class InternalFiscalIssuanceVoidEndpoints
{
    /// <summary>
    /// Maps internal fiscal issuance void command endpoints.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The same endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapInternalFiscalIssuanceVoidEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/fiscal-issuance")
            .WithTags("InternalFiscalIssuance")
            .RequireInternalServiceMtls();

        group.MapPost("/references/{fiscalIssuanceReferenceId:guid}/void", VoidAsync)
            .WithName("InternalFiscalIssuanceReferenceVoid")
            .Produces<FiscalIssuanceVoidCommandResponse>(StatusCodes.Status200OK)
            .Produces<FiscalIssuanceVoidCommandResponse>(StatusCodes.Status400BadRequest)
            .Produces<FiscalIssuanceVoidCommandResponse>(StatusCodes.Status404NotFound)
            .Produces<FiscalIssuanceVoidCommandResponse>(StatusCodes.Status409Conflict)
            .Produces<FiscalIssuanceVoidCommandResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> VoidAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceVoidCommandRequest request,
        IFiscalIssuanceVoidCommandService service,
        CancellationToken cancellationToken)
    {
        var response = await service.VoidAsync(
                fiscalIssuanceReferenceId,
                request,
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(response, statusCode: response.HttpStatusCode);
    }
}
