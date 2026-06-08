using System.Diagnostics;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console access readiness endpoint.
///
/// Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
/// Invariant enforced: Operator Console controlled actions require operator, device, shift, site,
/// workflow-state, and audit readiness before production use.
/// </summary>
public static class OperatorConsoleAccessReadinessEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleAccessReadiness");

    /// <summary>Maps Operator Console access readiness endpoints.</summary>
    public static IEndpointRouteBuilder MapOperatorConsoleAccessReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapPost("/access/readiness/evaluate", EvaluateAsync)
            .WithName("EvaluateOperatorConsoleAccessReadiness")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleAccessReadinessRequest>("application/json")
            .Produces<OperatorConsoleAccessReadinessResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Evaluate Operator Console access readiness")
            .WithDescription("Evaluates Operator Console access readiness and returns stable readiness dimensions and denial reasons. This endpoint does not mutate statutory discount, payment, provider, gate, coupon, settlement, reconciliation, or WebPay state.");

        return app;
    }

    private static IResult EvaluateAsync(
        OperatorConsoleAccessReadinessRequest request,
        OperatorConsoleAccessReadinessService service,
        IWebHostEnvironment environment,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP EvaluateOperatorConsoleAccessReadiness", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleAccessReadinessEndpoints");
        var correlationId = request.CorrelationId ?? Guid.Empty;

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("requested_action", request.RequestedAction);

        try
        {
            if (string.IsNullOrWhiteSpace(request.RequestedAction))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "RequestedAction is required.");
                return Results.BadRequest(BuildError(
                    "INVALID_OPERATOR_ACCESS_READINESS_REQUEST",
                    "RequestedAction is required.",
                    correlationId));
            }

            var result = service.Evaluate(new OperatorConsoleAccessReadinessCommand(
                request.OperatorUserId,
                request.OperatorDeviceBindingId,
                request.OperatorShiftId,
                request.SiteId,
                request.SiteGroupId,
                request.RequestedAction,
                request.TargetEntityType,
                request.TargetEntityId,
                request.WorkflowState,
                correlationId,
                request.DevModeContext?.EnvironmentName ?? environment.EnvironmentName,
                request.DevModeContext?.UsesLocalDevFallbackContext ?? false));

            activity?.SetTag("access_readiness_decision", result.AccessDecision);
            activity?.SetTag("access_readiness_allowed", result.AccessAllowed);
            activity?.SetTag("access_readiness_status", result.ReadinessStatus);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console access readiness evaluated. requested_action={RequestedAction} decision={Decision} allowed={Allowed} correlation_id={CorrelationId}",
                request.RequestedAction,
                result.AccessDecision,
                result.AccessAllowed,
                result.CorrelationId);

            return Results.Ok(ToContract(result));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console access readiness evaluation failed.");

            return Results.Json(
                BuildError(
                    "OPERATOR_ACCESS_READINESS_EVALUATION_FAILED",
                    "The Operator Console access readiness evaluation could not be completed.",
                    correlationId),
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

    private static OperatorConsoleAccessReadinessResponse ToContract(OperatorConsoleAccessReadinessResult result) =>
        new(
            AccessEvaluationId: null,
            result.AccessAllowed,
            result.AccessDecision,
            result.WorkflowReadiness.RequestedAction,
            result.ReadinessStatus,
            result.ReadinessDimensions.Select(dimension => new OperatorConsoleReadinessDimensionDto(
                dimension.Dimension,
                dimension.Status,
                dimension.Required,
                dimension.DenialReasonCodes)).ToArray(),
            result.DenialReasons.Select(reason => new OperatorConsoleAccessReadinessDenialReasonDto(
                reason.Code,
                reason.Severity,
                reason.Retryable,
                reason.UxMessageCategory)).ToArray(),
            new OperatorConsoleOperatorReadinessDto(
                result.OperatorReadiness.OperatorUserId,
                result.OperatorReadiness.Status,
                result.OperatorReadiness.Ready),
            new OperatorConsoleDeviceReadinessDto(
                result.DeviceReadiness.OperatorDeviceBindingId,
                result.DeviceReadiness.Status,
                result.DeviceReadiness.Ready),
            new OperatorConsoleShiftReadinessDto(
                result.ShiftReadiness.OperatorShiftId,
                result.ShiftReadiness.Status,
                result.ShiftReadiness.Ready),
            new OperatorConsoleSiteReadinessDto(
                result.SiteReadiness.SiteId,
                result.SiteReadiness.SiteGroupId,
                result.SiteReadiness.Status,
                result.SiteReadiness.Ready),
            new OperatorConsoleWorkflowReadinessDto(
                result.WorkflowReadiness.RequestedAction,
                result.WorkflowReadiness.WorkflowState,
                result.WorkflowReadiness.Status,
                result.WorkflowReadiness.Ready),
            result.AuditPersisted,
            result.EvaluatedAt,
            result.CorrelationId,
            result.Retryable,
            result.NextOperatorAction);
}
