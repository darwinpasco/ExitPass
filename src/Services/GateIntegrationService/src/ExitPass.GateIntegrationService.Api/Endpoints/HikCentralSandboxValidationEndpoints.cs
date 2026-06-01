using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

namespace ExitPass.GateIntegrationService.Api.Endpoints;

/// <summary>
/// Internal-only HikCentral sandbox validation endpoints.
/// </summary>
public static class HikCentralSandboxValidationEndpoints
{
    /// <summary>
    /// Maps explicitly gated HikCentral sandbox validation endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapHikCentralSandboxValidationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/hikcentral/sandbox")
            .WithTags("InternalHikCentralSandbox");

        group.MapPost("/validate-gate-action", HandleAsync)
            .WithName("ValidateHikCentralSandboxGateAction")
            .Produces<HikCentralSandboxValidationReport>(StatusCodes.Status200OK)
            .Produces<HikCentralSandboxValidationReport>(StatusCodes.Status400BadRequest)
            .Produces<HikCentralSandboxValidationReport>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HikCentralSandboxValidationRequest request,
        IHikCentralSandboxValidationHarness harness,
        CancellationToken cancellationToken)
    {
        var report = await harness.ValidateGateActionAsync(request, cancellationToken);
        if (report.Executed)
        {
            return Results.Ok(report);
        }

        return report.ResultCode switch
        {
            "HIKCENTRAL_SANDBOX_VALIDATION_REQUEST_INVALID" => Results.BadRequest(report),
            _ => Results.Conflict(report)
        };
    }
}
