using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Reconciliation;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator endpoints for reconciliation exception workflow review.
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
/// - Reconciliation records are operational workflow evidence, not payment authority.
/// - These endpoints never create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, consume gate authorization, or mutate provider outcome truth.
/// - Correlation metadata is required for write operations.
/// </summary>
public static class ReconciliationWorkflowEndpoints
{
    private const string ViewerPolicy = "ReconciliationViewer";
    private const string ReviewerPolicy = "ReconciliationReviewer";
    private const string ApproverPolicy = "ReconciliationApprover";

    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.Reconciliation");

    /// <summary>
    /// Maps Central PMS reconciliation workflow endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapReconciliationWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/reconciliation")
            .WithTags("OpsReconciliation");

        group.MapPost("/items/{reconciliationItemId:guid}/notes", AddNoteAsync)
            .WithName("AddReconciliationNote")
            .WithMetadata(new ReconciliationPolicyMetadata(ReviewerPolicy))
            .Produces<AddReconciliationNoteResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/items/{reconciliationItemId:guid}/resolution-requests", SubmitResolutionRequestAsync)
            .WithName("SubmitReconciliationResolutionRequest")
            .WithMetadata(new ReconciliationPolicyMetadata(ReviewerPolicy))
            .Produces<SubmitReconciliationResolutionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/resolution-requests/{resolutionRequestId:guid}/decision", DecideResolutionRequestAsync)
            .WithName("DecideReconciliationResolutionRequest")
            .WithMetadata(new ReconciliationPolicyMetadata(ApproverPolicy))
            .Produces<DecideReconciliationResolutionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/items/{reconciliationItemId:guid}/workflow-history", ReadWorkflowHistoryAsync)
            .WithName("GetReconciliationWorkflowHistory")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<ReconciliationWorkflowHistoryResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/runs", ListRunsAsync)
            .WithName("ListReconciliationRuns")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<ReconciliationRunsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/exceptions", ListExceptionsAsync)
            .WithName("ListReconciliationExceptions")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<ReconciliationExceptionsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> AddNoteAsync(
        Guid reconciliationItemId,
        AddReconciliationNoteRequest request,
        HttpRequest httpRequest,
        IReconciliationWorkflowService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP AddReconciliationNote", httpRequest, reconciliationItemId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationWorkflowEndpoints");

        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.AddNoteAsync(
                new AddReconciliationNoteCommand(
                    reconciliationItemId,
                    request.NoteText,
                    string.IsNullOrWhiteSpace(request.NoteType) ? "REVIEW_NOTE" : request.NoteType,
                    request.ActorUserId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("reconciliation_exception_id", result.ReconciliationExceptionId);

            logger.LogInformation(
                "Reconciliation note added. reconciliation_item_id={ReconciliationItemId} reconciliation_exception_id={ReconciliationExceptionId}",
                result.ReconciliationItemId,
                result.ReconciliationExceptionId);

            return Results.Created(
                $"/v1/ops/reconciliation/items/{result.ReconciliationItemId}/workflow-history",
                new AddReconciliationNoteResponse(
                    result.ReconciliationItemId,
                    result.ReconciliationExceptionId,
                    result.ReconciliationExceptionNoteId,
                    result.NoteType,
                    result.CreatedAt,
                    result.CorrelationId));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> SubmitResolutionRequestAsync(
        Guid reconciliationItemId,
        SubmitReconciliationResolutionRequest request,
        HttpRequest httpRequest,
        IReconciliationWorkflowService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP SubmitReconciliationResolutionRequest", httpRequest, reconciliationItemId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationWorkflowEndpoints");

        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.SubmitResolutionRequestAsync(
                new SubmitReconciliationResolutionCommand(
                    reconciliationItemId,
                    request.ResolutionAction,
                    request.ResolutionReason,
                    request.FinancialImpact,
                    request.AdjustmentRequired,
                    string.IsNullOrWhiteSpace(request.RequestSummary)
                        ? request.ResolutionReason
                        : request.RequestSummary,
                    request.RequestDetail,
                    string.IsNullOrWhiteSpace(request.ProposedExceptionStatus)
                        ? "RESOLVED"
                        : request.ProposedExceptionStatus,
                    request.ActorUserId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("reconciliation_exception_id", result.ReconciliationExceptionId);
            activity?.SetTag("resolution_request_id", result.ResolutionRequestId);

            return Results.Created(
                $"/v1/ops/reconciliation/items/{result.ReconciliationItemId}/workflow-history",
                new SubmitReconciliationResolutionResponse(
                    result.ReconciliationItemId,
                    result.ReconciliationExceptionId,
                    result.ResolutionRequestId,
                    result.RequestStatus,
                    result.PreviousExceptionStatus,
                    result.ProposedExceptionStatus,
                    result.SubmittedAt,
                    result.CorrelationId));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> DecideResolutionRequestAsync(
        Guid resolutionRequestId,
        DecideReconciliationResolutionRequest request,
        HttpRequest httpRequest,
        IReconciliationWorkflowService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP DecideReconciliationResolutionRequest", httpRequest, resolutionRequestId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationWorkflowEndpoints");

        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var decision = string.IsNullOrWhiteSpace(request.Decision) ? "APPROVED" : request.Decision;
            var result = await service.DecideResolutionRequestAsync(
                new DecideReconciliationResolutionCommand(
                    resolutionRequestId,
                    decision,
                    request.Reason,
                    request.Comment,
                    request.ActorUserId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("reconciliation_exception_id", result.ReconciliationExceptionId);
            activity?.SetTag("resolution_request_id", result.ResolutionRequestId);
            activity?.SetTag("decision", result.Decision);

            return Results.Ok(new DecideReconciliationResolutionResponse(
                result.ResolutionRequestId,
                result.ReconciliationExceptionId,
                result.ResolutionApprovalId,
                result.Decision,
                result.RequestStatus,
                result.ExceptionStatus,
                result.DecidedAt,
                result.CorrelationId));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ReadWorkflowHistoryAsync(
        Guid reconciliationItemId,
        IReconciliationWorkflowService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetReconciliationWorkflowHistory", httpRequest, reconciliationItemId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationWorkflowEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var records = await service.ReadWorkflowHistoryAsync(
                new ReadReconciliationWorkflowHistoryQuery(reconciliationItemId),
                cancellationToken);

            var response = new ReconciliationWorkflowHistoryResponse(
                reconciliationItemId,
                records.Select(ToContract).ToArray());

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ListRunsAsync(
        int? limit,
        IReconciliationWorkflowService service,
        CancellationToken cancellationToken)
    {
        var records = await service.ListRunsAsync(new ListReconciliationRunsQuery(limit ?? 20), cancellationToken);
        return Results.Ok(new ReconciliationRunsResponse(records.Select(ToContract).ToArray()));
    }

    private static async Task<IResult> ListExceptionsAsync(
        int? limit,
        string? status,
        string? severity,
        Guid? runId,
        IReconciliationWorkflowService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP ListReconciliationExceptions", httpRequest, runId ?? Guid.Empty);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationWorkflowEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var records = await service.ListExceptionsAsync(
                new ListReconciliationExceptionsQuery(limit ?? 20, status, severity, runId),
                cancellationToken);

            return Results.Ok(new ReconciliationExceptionsResponse(records.Select(ToContract).ToArray()));
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
        activity?.SetTag("reconciliation_entity_id", entityId);
        return activity;
    }

    private static bool TryGetCorrelationId(
        HttpRequest request,
        out IResult error,
        out Guid correlationId)
    {
        if (!request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) ||
            !Guid.TryParse(headerValue.ToString(), out correlationId))
        {
            correlationId = Guid.Empty;
            error = Results.BadRequest(BuildError(
                "CORRELATION_ID_REQUIRED",
                "X-Correlation-Id header is required.",
                Guid.Empty,
                retryable: false));
            return false;
        }

        error = Results.Empty;
        return true;
    }

    private static Guid ResolveCorrelationId(HttpRequest request)
    {
        return request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var correlationId)
            ? correlationId
            : Guid.Empty;
    }

    private static IResult MapException(
        Exception exception,
        Guid correlationId,
        Activity? activity,
        ILogger logger)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);

        return exception switch
        {
            ArgumentException ex => Results.BadRequest(BuildError(
                "INVALID_REQUEST",
                ex.Message,
                correlationId,
                retryable: false)),
            ReconciliationExceptionNotFoundException ex => Results.NotFound(BuildError(
                "RECONCILIATION_EXCEPTION_NOT_FOUND",
                ex.Message,
                correlationId,
                retryable: false)),
            ReconciliationResolutionRequestNotFoundException ex => Results.NotFound(BuildError(
                "RECONCILIATION_RESOLUTION_REQUEST_NOT_FOUND",
                ex.Message,
                correlationId,
                retryable: false)),
            ReconciliationWorkflowConflictException ex => Results.Conflict(BuildError(
                ex.ErrorCode,
                ex.Message,
                correlationId,
                retryable: false)),
            _ => Unexpected(exception, correlationId, logger)
        };
    }

    private static IResult Unexpected(Exception exception, Guid correlationId, ILogger logger)
    {
        logger.LogError(exception, "Unexpected reconciliation workflow API failure.");

        return Results.Json(
            BuildError(
                "RECONCILIATION_WORKFLOW_INTERNAL_ERROR",
                "An unexpected error occurred while processing the reconciliation workflow request.",
                correlationId,
                retryable: false),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable)
    {
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };
    }

    private static ReconciliationWorkflowHistoryEntry ToContract(ReconciliationWorkflowHistoryRecord record) =>
        new(
            record.RecordType,
            record.ReconciliationExceptionId,
            record.ReconciliationExceptionNoteId,
            record.ResolutionRequestId,
            record.ResolutionApprovalId,
            record.StatusHistoryId,
            record.ReconciliationRunId,
            record.ReconciliationItemId,
            record.Status,
            record.ReasonCode,
            record.Summary,
            record.Detail,
            record.ActorUserId,
            record.OccurredAt,
            record.CorrelationId);

    private static ReconciliationRunSummary ToContract(ReconciliationRunRecord record) =>
        new(
            record.ReconciliationRunId,
            record.RunCode,
            record.RunType,
            record.RunStatus,
            record.ScopeType,
            record.SourceBatchRef,
            record.StartedAt,
            record.CompletedAt,
            record.ItemCount,
            record.MatchedCount,
            record.ExceptionCount,
            record.CorrelationId);

    private static ReconciliationExceptionSummary ToContract(ReconciliationExceptionRecord record) =>
        new(
            record.ReconciliationExceptionId,
            record.ReconciliationRunId,
            record.ReconciliationItemId,
            record.RunCode,
            record.ExceptionType,
            record.ExceptionSeverity,
            record.ExceptionStatus,
            record.ExceptionReasonCode,
            record.ExceptionSummary,
            record.PaymentAttemptId,
            record.PaymentConfirmationId,
            record.TargetEntityType,
            record.TargetEntityId,
            record.DetectedAt,
            record.CorrelationId);
}
