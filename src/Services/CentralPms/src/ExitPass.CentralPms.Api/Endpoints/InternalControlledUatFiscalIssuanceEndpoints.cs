using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal controlled-UAT-only fiscal issuance diagnostic endpoints.
///
/// This surface is intentionally narrow: it invokes the existing guarded
/// application-level UAT harness and must not be exposed as a production
/// operator action or wired into payment, exit, or gate flows.
/// </summary>
public static class InternalControlledUatFiscalIssuanceEndpoints
{
    /// <summary>
    /// Maps internal controlled-UAT fiscal issuance diagnostic endpoints.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The same endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapInternalControlledUatFiscalIssuanceEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/controlled-uat/fiscal-issuance")
            .WithTags("InternalControlledUatFiscalIssuance")
            .RequireInternalServiceMtls();

        group.MapPost("/preflight", PreflightAsync)
            .WithName("ControlledUatFiscalIssuancePreflight")
            .Produces<ControlledUatFiscalIssuanceInvocationResponse>(StatusCodes.Status200OK)
            .Produces<ControlledUatFiscalIssuanceInvocationResponse>(StatusCodes.Status400BadRequest)
            .Produces<ControlledUatFiscalIssuanceInvocationResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/run", RunAsync)
            .WithName("ControlledUatFiscalIssuanceRun")
            .Produces<ControlledUatFiscalIssuanceInvocationResponse>(StatusCodes.Status200OK)
            .Produces<ControlledUatFiscalIssuanceInvocationResponse>(StatusCodes.Status400BadRequest)
            .Produces<ControlledUatFiscalIssuanceInvocationResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/void-smoke", VoidSmokeAsync)
            .WithName("ControlledUatFiscalVoidSmoke")
            .Produces<ControlledUatFiscalVoidSmokeResponse>(StatusCodes.Status200OK)
            .Produces<ControlledUatFiscalVoidSmokeResponse>(StatusCodes.Status400BadRequest)
            .Produces<ControlledUatFiscalVoidSmokeResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> PreflightAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        IFiscalIssuanceControlledUatInvocationService service,
        CancellationToken cancellationToken)
    {
        var response = await service.PreflightAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(response, statusCode: response.HttpStatusCode);
    }

    private static async Task<IResult> RunAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        IFiscalIssuanceControlledUatInvocationService service,
        CancellationToken cancellationToken)
    {
        var response = await service.RunAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(response, statusCode: response.HttpStatusCode);
    }

    private static async Task<IResult> VoidSmokeAsync(
        ControlledUatFiscalVoidSmokeRequest request,
        IFiscalIssuanceControlledUatVoidSmokeService service,
        CancellationToken cancellationToken)
    {
        var response = await service.RunAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(response, statusCode: response.HttpStatusCode);
    }
}
