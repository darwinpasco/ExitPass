using System.Diagnostics;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console access evaluation endpoint.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This endpoint persists Operator Console access evaluation evidence only.
/// - This endpoint never creates or mutates PaymentAttempt, PaymentConfirmation, ExitAuthorization,
///   provider outcome, gate consume, coupon application, settlement truth, reconciliation records, or payment finality.
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
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleAccessEvaluationRequest>("application/json")
            .Produces<OperatorConsoleAccessEvaluationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Evaluate Operator Console access")
            .WithDescription("Evaluates and persists whether an Operator Console user may perform a controlled action. This endpoint persists access evaluation evidence only and does not mutate payment, gate, coupon, provider, settlement, or reconciliation state.");

        return app;
    }

    private static async Task<IResult> EvaluateAsync(
        OperatorConsoleAccessEvaluationRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleAccessEvaluationService service,
        IOperatorConsoleAccessEvaluationWriter writer,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP EvaluateOperatorConsoleAccess", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleAccessEvaluationEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("workflow_code", request.WorkflowCode);
        activity?.SetTag("controlled_action_code", request.ControlledActionCode);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.UserId,
                request.OperatorDeviceBindingId,
                request.OperatorShiftId,
                request.SiteId,
                request.SiteGroupId,
                request.CorrelationId);

            var result = await service.EvaluateAsync(
                new OperatorConsoleAccessEvaluationCommand(
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    request.WorkflowCode,
                    request.ControlledActionCode,
                    request.ParkingSessionId,
                    request.EvidenceAccessIntent,
                    request.IdempotencyKey,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            var persistedResult = await writer.PersistAsync(result, httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("access_evaluation_decision", persistedResult.Decision);
            activity?.SetTag("access_evaluation_allowed", persistedResult.Allowed);
            activity?.SetTag("access_evaluation_persisted", persistedResult.Persisted);
            activity?.SetTag("operator_access_evaluation_id", persistedResult.EvaluationId);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console access evaluated and persisted. evaluation_id={EvaluationId} workflow_code={WorkflowCode} controlled_action_code={ControlledActionCode} decision={Decision} allowed={Allowed}",
                persistedResult.EvaluationId,
                request.WorkflowCode,
                request.ControlledActionCode,
                persistedResult.Decision,
                persistedResult.Allowed);

            return Results.Ok(ToContract(persistedResult));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_ACCESS_EVALUATION_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console access evaluation persistence failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_ACCESS_EVALUATION_PERSISTENCE_FAILED",
                    "The Operator Console access evaluation could not be persisted.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };

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
