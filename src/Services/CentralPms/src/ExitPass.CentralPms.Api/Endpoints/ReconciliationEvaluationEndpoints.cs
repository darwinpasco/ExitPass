using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Reconciliation;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator endpoints for conservative reconciliation item evaluation.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - Reconciliation evaluation is operational evidence, not payment authority.
/// - These endpoints never create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, consume gate authorization, mutate provider outcome truth, or infer settlement completion.
/// - Correlation metadata is required for evaluation writes.
/// </summary>
public static class ReconciliationEvaluationEndpoints
{
    private const string EvaluatorPolicy = "ReconciliationItemEvaluator";
    private const string ViewerPolicy = "ReconciliationEvaluationViewer";

    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ReconciliationEvaluation");

    /// <summary>
    /// Maps Central PMS reconciliation evaluation endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapReconciliationEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/reconciliation")
            .WithTags("OpsReconciliation");

        group.MapPost("/items/{reconciliationItemId:guid}/evaluate", EvaluateAsync)
            .WithName("EvaluateReconciliationItem")
            .WithMetadata(new ReconciliationPolicyMetadata(EvaluatorPolicy))
            .Produces<ReconciliationItemEvaluationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/items/{reconciliationItemId:guid}/evaluation", ReadEvaluationAsync)
            .WithName("GetReconciliationItemEvaluation")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<ReconciliationItemEvaluationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> EvaluateAsync(
        Guid reconciliationItemId,
        EvaluateReconciliationItemRequest request,
        HttpRequest httpRequest,
        IReconciliationEvaluationService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP EvaluateReconciliationItem", httpRequest, reconciliationItemId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationEvaluationEndpoints");

        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.EvaluateAsync(
                new EvaluateReconciliationItemCommand(
                    reconciliationItemId,
                    request.ActorUserId,
                    request.ServiceIdentityId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("reconciliation_item_id", result.ReconciliationItemId);
            activity?.SetTag("match_status", result.MatchStatus);

            logger.LogInformation(
                "Reconciliation item evaluated. reconciliation_item_id={ReconciliationItemId} match_status={MatchStatus} item_status={ItemStatus}",
                result.ReconciliationItemId,
                result.MatchStatus,
                result.ItemStatus);

            return Results.Ok(ToContract(result));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ReadEvaluationAsync(
        Guid reconciliationItemId,
        IReconciliationEvaluationService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetReconciliationItemEvaluation", httpRequest, reconciliationItemId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationEvaluationEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var result = await service.ReadEvaluationAsync(
                new ReadReconciliationItemEvaluationQuery(reconciliationItemId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static Activity? StartActivity(string activityName, HttpRequest request, Guid entityId)
    {
        var activity = ActivitySource.StartActivity(activityName, ActivityKind.Server);
        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        activity?.SetTag("reconciliation_item_id", entityId);
        return activity;
    }

    private static bool TryGetCorrelationId(HttpRequest request, out IResult error, out Guid correlationId)
    {
        if (!request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) ||
            !Guid.TryParse(headerValue.ToString(), out correlationId))
        {
            correlationId = Guid.Empty;
            error = Results.BadRequest(BuildError("CORRELATION_ID_REQUIRED", "X-Correlation-Id header is required.", Guid.Empty, retryable: false));
            return false;
        }

        error = Results.Empty;
        return true;
    }

    private static Guid ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
        Guid.TryParse(headerValue.ToString(), out var correlationId)
            ? correlationId
            : Guid.Empty;

    private static IResult MapException(Exception exception, Guid correlationId, Activity? activity, ILogger logger)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);

        return exception switch
        {
            ArgumentException ex => Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId, retryable: false)),
            ReconciliationItemNotFoundException ex => Results.NotFound(BuildError("RECONCILIATION_ITEM_NOT_FOUND", ex.Message, correlationId, retryable: false)),
            _ => Unexpected(exception, correlationId, logger)
        };
    }

    private static IResult Unexpected(Exception exception, Guid correlationId, ILogger logger)
    {
        logger.LogError(exception, "Unexpected reconciliation evaluation API failure.");
        return Results.Json(
            BuildError(
                "RECONCILIATION_EVALUATION_INTERNAL_ERROR",
                "An unexpected error occurred while processing the reconciliation evaluation request.",
                correlationId,
                retryable: false),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId, bool retryable) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };

    private static ReconciliationItemEvaluationResponse ToContract(ReconciliationItemEvaluationRecord record) =>
        new(
            record.ReconciliationItemId,
            record.ReconciliationRunId,
            record.ComparisonBasis,
            record.ItemStatus,
            record.MatchStatus,
            record.EvaluationClassification,
            record.EvaluationReason,
            record.ExpectedAmount,
            record.ActualAmount,
            record.VarianceAmount,
            record.ExceptionReasonCode,
            record.ExceptionCreatedOrUpdated,
            record.ExceptionHandling,
            record.EvaluatedAt,
            record.CorrelationId);
}
