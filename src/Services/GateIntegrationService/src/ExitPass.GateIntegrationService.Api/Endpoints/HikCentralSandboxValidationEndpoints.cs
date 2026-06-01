using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using ExitPass.GateIntegrationService.Api.Security;

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
            .Produces<HikCentralSandboxValidationReport>(StatusCodes.Status401Unauthorized)
            .Produces<HikCentralSandboxValidationReport>(StatusCodes.Status403Forbidden)
            .Produces<HikCentralSandboxValidationReport>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HikCentralSandboxValidationRequest request,
        HttpRequest httpRequest,
        HikCentralSandboxValidationAccessValidator accessValidator,
        IHikCentralSandboxValidationHarness harness,
        CancellationToken cancellationToken)
    {
        var accessDecision = accessValidator.Validate(httpRequest, request);
        if (!accessDecision.IsAllowed)
        {
            return Results.Json(
                accessDecision.DenialReport,
                statusCode: accessDecision.StatusCode);
        }

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
