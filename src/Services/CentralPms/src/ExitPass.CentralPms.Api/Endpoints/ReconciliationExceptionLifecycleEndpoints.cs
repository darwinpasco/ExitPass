using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Reconciliation;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator endpoints for reconciliation exception lifecycle control.
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
/// - Reconciliation exception lifecycle is operational control only, not payment authority.
/// - These endpoints never create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, consume gate authorization, mutate provider outcome truth, or close settlement truth.
/// - Correlation metadata is required for lifecycle write operations.
/// </summary>
public static class ReconciliationExceptionLifecycleEndpoints
{
    private const string ViewerPolicy = "ReconciliationExceptionViewer";
    private const string AssignmentPolicy = "ReconciliationExceptionAssignment";
    private const string StatusPolicy = "ReconciliationExceptionStatusUpdate";
    private const string ResolutionPolicy = "ReconciliationExceptionResolution";
    private const string RejectionPolicy = "ReconciliationExceptionRejection";
    private const string EscalationPolicy = "ReconciliationExceptionEscalation";
    private const string ClosurePolicy = "ReconciliationExceptionClosure";

    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ReconciliationExceptionLifecycle");

    /// <summary>
    /// Maps Central PMS reconciliation exception lifecycle endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapReconciliationExceptionLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/reconciliation")
            .WithTags("OpsReconciliation");

        group.MapGet("/exceptions/{reconciliationExceptionId:guid}", ReadExceptionAsync)
            .WithName("GetReconciliationException")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<ReconciliationExceptionDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/exceptions/{reconciliationExceptionId:guid}/assign", AssignAsync)
            .WithName("AssignReconciliationException")
            .WithMetadata(new ReconciliationPolicyMetadata(AssignmentPolicy))
            .Produces<ReconciliationExceptionLifecycleResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/exceptions/{reconciliationExceptionId:guid}/status", UpdateStatusAsync)
            .WithName("UpdateReconciliationExceptionStatus")
            .WithMetadata(new ReconciliationPolicyMetadata(StatusPolicy))
            .Produces<ReconciliationExceptionLifecycleResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/exceptions/{reconciliationExceptionId:guid}/resolve", ResolveAsync)
            .WithName("ResolveReconciliationException")
            .WithMetadata(new ReconciliationPolicyMetadata(ResolutionPolicy))
            .Produces<ReconciliationExceptionLifecycleResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/exceptions/{reconciliationExceptionId:guid}/reject", RejectAsync)
            .WithName("RejectReconciliationException")
            .WithMetadata(new ReconciliationPolicyMetadata(RejectionPolicy))
            .Produces<ReconciliationExceptionLifecycleResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/exceptions/{reconciliationExceptionId:guid}/escalate", EscalateAsync)
            .WithName("EscalateReconciliationException")
            .WithMetadata(new ReconciliationPolicyMetadata(EscalationPolicy))
            .Produces<ReconciliationExceptionLifecycleResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/exceptions/{reconciliationExceptionId:guid}/close", CloseAsync)
            .WithName("CloseReconciliationException")
            .WithMetadata(new ReconciliationPolicyMetadata(ClosurePolicy))
            .Produces<ReconciliationExceptionLifecycleResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> ReadExceptionAsync(
        Guid reconciliationExceptionId,
        IReconciliationExceptionLifecycleService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetReconciliationException", httpRequest, reconciliationExceptionId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationExceptionLifecycleEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var record = await service.ReadAsync(new ReadReconciliationExceptionQuery(reconciliationExceptionId), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(record));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> AssignAsync(
        Guid reconciliationExceptionId,
        AssignReconciliationExceptionRequest request,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP AssignReconciliationException", httpRequest, reconciliationExceptionId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationExceptionLifecycleEndpoints");
        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.AssignAsync(
                new AssignReconciliationExceptionCommand(
                    reconciliationExceptionId,
                    request.AssignedToUserId,
                    request.AssignedToServiceIdentityId,
                    request.ReasonCode,
                    request.Detail,
                    request.ActorUserId,
                    request.ServiceIdentityId,
                    correlationId),
                cancellationToken);
            return OkLifecycle(result, activity, logger);
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static Task<IResult> UpdateStatusAsync(
        Guid reconciliationExceptionId,
        UpdateReconciliationExceptionStatusRequest request,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        LifecycleAsync(
            reconciliationExceptionId,
            request.Status,
            "STATUS_UPDATE",
            request.ReasonCode,
            request.Detail,
            request.ActorUserId,
            request.ServiceIdentityId,
            httpRequest,
            service,
            loggerFactory,
            cancellationToken);

    private static Task<IResult> ResolveAsync(
        Guid reconciliationExceptionId,
        ReconciliationExceptionLifecycleRequest request,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        LifecycleAsync(reconciliationExceptionId, "RESOLVED", "RESOLVE", request.ReasonCode, request.Detail, request.ActorUserId, request.ServiceIdentityId, httpRequest, service, loggerFactory, cancellationToken);

    private static Task<IResult> RejectAsync(
        Guid reconciliationExceptionId,
        ReconciliationExceptionLifecycleRequest request,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        LifecycleAsync(reconciliationExceptionId, "REJECTED", "REJECT", request.ReasonCode, request.Detail, request.ActorUserId, request.ServiceIdentityId, httpRequest, service, loggerFactory, cancellationToken);

    private static Task<IResult> EscalateAsync(
        Guid reconciliationExceptionId,
        ReconciliationExceptionLifecycleRequest request,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        LifecycleAsync(reconciliationExceptionId, "ESCALATED", "ESCALATE", request.ReasonCode, request.Detail, request.ActorUserId, request.ServiceIdentityId, httpRequest, service, loggerFactory, cancellationToken);

    private static Task<IResult> CloseAsync(
        Guid reconciliationExceptionId,
        ReconciliationExceptionLifecycleRequest request,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        LifecycleAsync(reconciliationExceptionId, "CLOSED", "CLOSE", request.ReasonCode, request.Detail, request.ActorUserId, request.ServiceIdentityId, httpRequest, service, loggerFactory, cancellationToken);

    private static async Task<IResult> LifecycleAsync(
        Guid reconciliationExceptionId,
        string newStatus,
        string action,
        string reasonCode,
        string? detail,
        Guid? actorUserId,
        Guid? serviceIdentityId,
        HttpRequest httpRequest,
        IReconciliationExceptionLifecycleService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity($"HTTP {action}ReconciliationException", httpRequest, reconciliationExceptionId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationExceptionLifecycleEndpoints");
        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.UpdateStatusAsync(
                new UpdateReconciliationExceptionStatusCommand(
                    reconciliationExceptionId,
                    newStatus,
                    action,
                    reasonCode,
                    detail,
                    actorUserId,
                    serviceIdentityId,
                    correlationId),
                cancellationToken);
            return OkLifecycle(result, activity, logger);
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static IResult OkLifecycle(
        ReconciliationExceptionLifecycleResult result,
        Activity? activity,
        ILogger logger)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("reconciliation_exception_id", result.ReconciliationExceptionId);
        activity?.SetTag("previous_status", result.PreviousStatus);
        activity?.SetTag("current_status", result.CurrentStatus);
        logger.LogInformation(
            "Reconciliation exception lifecycle action completed. reconciliation_exception_id={ReconciliationExceptionId} action={Action} previous_status={PreviousStatus} current_status={CurrentStatus}",
            result.ReconciliationExceptionId,
            result.Action,
            result.PreviousStatus,
            result.CurrentStatus);
        return Results.Ok(new ReconciliationExceptionLifecycleResponse(
            result.ReconciliationExceptionId,
            result.PreviousStatus,
            result.CurrentStatus,
            result.Action,
            result.UpdatedAt,
            result.CorrelationId));
    }

    private static Activity? StartActivity(string activityName, HttpRequest request, Guid entityId)
    {
        var activity = ActivitySource.StartActivity(activityName, ActivityKind.Server);
        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        activity?.SetTag("reconciliation_exception_id", entityId);
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
            ReconciliationExceptionNotFoundException ex => Results.NotFound(BuildError("RECONCILIATION_EXCEPTION_NOT_FOUND", ex.Message, correlationId, retryable: false)),
            ReconciliationWorkflowConflictException ex => Results.Conflict(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false)),
            ReconciliationRunItemRejectedException ex when ex.ErrorCode.EndsWith("_NOT_FOUND", StringComparison.OrdinalIgnoreCase) => Results.NotFound(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false)),
            ReconciliationRunItemRejectedException ex => Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false)),
            _ => Unexpected(exception, correlationId, logger)
        };
    }

    private static IResult Unexpected(Exception exception, Guid correlationId, ILogger logger)
    {
        logger.LogError(exception, "Unexpected reconciliation exception lifecycle API failure.");
        return Results.Json(
            BuildError(
                "RECONCILIATION_EXCEPTION_LIFECYCLE_INTERNAL_ERROR",
                "An unexpected error occurred while processing the reconciliation exception lifecycle request.",
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

    private static ReconciliationExceptionDetailResponse ToContract(ReconciliationExceptionDetailRecord record) =>
        new(
            record.ReconciliationExceptionId,
            record.ReconciliationRunId,
            record.ReconciliationItemId,
            record.IncidentRecordId,
            record.ExceptionType,
            record.ExceptionSeverity,
            record.ExceptionStatus,
            record.ExceptionReasonCode,
            record.ExceptionSummary,
            record.ExceptionDetail,
            record.AssignedToUserId,
            record.AssignedToServiceIdentityId,
            record.CreatedFromStatus,
            record.DetectedAt,
            record.AssignedAt,
            record.ResolvedAt,
            record.ClosedAt,
            record.ResolutionReasonCode,
            record.ClosureReasonCode,
            record.ResolvedByUserId,
            record.ResolvedByServiceIdentityId,
            record.ClosedByUserId,
            record.ClosedByServiceIdentityId,
            record.CreatedAt,
            record.UpdatedAt,
            record.CorrelationId);
}
