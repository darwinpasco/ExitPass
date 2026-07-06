using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal-only semantic hash backfill workflow request endpoint.
/// </summary>
public static class InternalFiscalExceptionQueueSemanticHashBackfillEndpoints
{
    /// <summary>
    /// Maps internal semantic hash backfill workflow request endpoints.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The same endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapInternalFiscalExceptionQueueSemanticHashBackfillEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/v1/fiscal-exception-queue")
            .WithTags("InternalFiscalExceptionQueue")
            .RequireInternalServiceMtls();

        group.MapPost("/semantic-hash-backfill-requests", RequestAsync)
            .WithName("RequestSemanticHashBackfillInternal")
            .Produces<FiscalExceptionSemanticHashBackfillInternalApiResponse>(StatusCodes.Status200OK)
            .Produces<FiscalExceptionSemanticHashBackfillInternalApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<FiscalExceptionSemanticHashBackfillInternalApiResponse>(StatusCodes.Status403Forbidden)
            .Produces<FiscalExceptionSemanticHashBackfillInternalApiResponse>(StatusCodes.Status404NotFound)
            .Produces<FiscalExceptionSemanticHashBackfillInternalApiResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> RequestAsync(
        FiscalExceptionSemanticHashBackfillInternalApiRequest request,
        IFiscalExceptionSemanticHashBackfillInternalApiHandler handler,
        CancellationToken cancellationToken)
    {
        var response = await handler.RequestAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(response, statusCode: response.HttpStatusCode);
    }
}
