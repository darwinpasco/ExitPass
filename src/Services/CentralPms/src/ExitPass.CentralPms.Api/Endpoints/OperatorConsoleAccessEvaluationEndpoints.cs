using System.Diagnostics;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console access evaluation endpoint skeleton.
///
/// This placeholder is intentionally fail-closed and must not be treated as production access logic.
/// The database-backed role, device, shift, takeover, site, and evidence-access checks are a later slice.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This skeleton never creates or mutates PaymentAttempt, PaymentConfirmation, ExitAuthorization,
///   provider outcome, gate consume, coupon application, settlement truth, or payment finality.
/// - This skeleton does not persist access evaluations.
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

    private static IResult EvaluateAsync(
        OperatorConsoleAccessEvaluationRequest request,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP EvaluateOperatorConsoleAccess", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleAccessEvaluationEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("workflow_code", request.WorkflowCode);
        activity?.SetTag("controlled_action_code", request.ControlledActionCode);
        activity?.SetTag("access_evaluation_decision", "NOT_IMPLEMENTED");
        activity?.SetStatus(ActivityStatusCode.Ok);

        logger.LogInformation(
            "Operator Console access evaluation skeleton denied request. workflow_code={WorkflowCode} controlled_action_code={ControlledActionCode}",
            request.WorkflowCode,
            request.ControlledActionCode);

        // Fail closed until the database-backed access evaluator is implemented. This response is a route/DTO
        // skeleton only and must not be interpreted as production authorization logic.
        var response = new OperatorConsoleAccessEvaluationResponse(
            EvaluationId: Guid.Empty,
            Allowed: false,
            Decision: "NOT_IMPLEMENTED",
            DenialReasons: new[] { "ACCESS_EVALUATION_NOT_IMPLEMENTED" },
            EffectiveRole: null,
            DeviceTrust: new OperatorConsoleDeviceTrustDto(
                request.OperatorDeviceBindingId,
                "NOT_EVALUATED",
                "UNKNOWN",
                Trusted: false),
            ShiftContext: new OperatorConsoleShiftContextDto(
                request.OperatorShiftId,
                "NOT_EVALUATED",
                Active: false),
            SiteContext: new OperatorConsoleSiteContextDto(
                request.SiteId,
                request.SiteGroupId,
                Assigned: false),
            EvaluatedAt: DateTimeOffset.UnixEpoch,
            Persisted: false,
            CorrelationId: request.CorrelationId);

        return Results.Ok(response);
    }
}
