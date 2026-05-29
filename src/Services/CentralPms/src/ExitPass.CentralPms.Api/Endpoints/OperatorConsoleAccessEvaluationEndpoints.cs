using System.Diagnostics;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console access evaluation endpoint.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This endpoint never creates or mutates PaymentAttempt, PaymentConfirmation, ExitAuthorization,
///   provider outcome, gate consume, coupon application, settlement truth, or payment finality.
/// - This endpoint does not persist access evaluations.
/// </summary>
public static class OperatorConsoleAccessEvaluationEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleAccessEvaluation");

    /// <summary>
    /// Maps Operator Console access evaluation endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleAccessEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapPost("/access/evaluate", EvaluateAsync)
            .WithName("EvaluateOperatorConsoleAccess")
            .Produces<OperatorConsoleAccessEvaluationResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> EvaluateAsync(
        OperatorConsoleAccessEvaluationRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleAccessEvaluationService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP EvaluateOperatorConsoleAccess", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleAccessEvaluationEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("workflow_code", request.WorkflowCode);
        activity?.SetTag("controlled_action_code", request.ControlledActionCode);

        var result = await service.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                request.UserId,
                request.OperatorDeviceBindingId,
                request.SiteId,
                request.SiteGroupId,
                request.OperatorShiftId,
                request.WorkflowCode,
                request.ControlledActionCode,
                request.ParkingSessionId,
                request.EvidenceAccessIntent,
                request.IdempotencyKey,
                request.CorrelationId),
            httpRequest.HttpContext.RequestAborted);

        activity?.SetTag("access_evaluation_decision", result.Decision);
        activity?.SetTag("access_evaluation_allowed", result.Allowed);
        activity?.SetStatus(ActivityStatusCode.Ok);

        logger.LogInformation(
            "Operator Console access evaluated. workflow_code={WorkflowCode} controlled_action_code={ControlledActionCode} decision={Decision} allowed={Allowed}",
            request.WorkflowCode,
            request.ControlledActionCode,
            result.Decision,
            result.Allowed);

        return Results.Ok(ToContract(result));
    }

    private static OperatorConsoleAccessEvaluationResponse ToContract(OperatorConsoleAccessEvaluationResult result) =>
        new(
            result.EvaluationId,
            result.Allowed,
            result.Decision,
            result.DenialReasons,
            result.EffectiveRole,
            new OperatorConsoleDeviceTrustDto(
                result.DeviceTrust.OperatorDeviceBindingId,
                result.DeviceTrust.Status,
                result.DeviceTrust.TrustLevel,
                result.DeviceTrust.Trusted),
            new OperatorConsoleShiftContextDto(
                result.ShiftContext.OperatorShiftId,
                result.ShiftContext.Status,
                result.ShiftContext.Active),
            new OperatorConsoleSiteContextDto(
                result.SiteContext.SiteId,
                result.SiteContext.SiteGroupId,
                result.SiteContext.Assigned),
            result.EvaluatedAt,
            result.Persisted,
            result.CorrelationId);
}
