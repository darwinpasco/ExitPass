using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Reconciliation;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator endpoints for reconciliation run creation and item readback.
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
/// - Reconciliation run and item APIs are operational evidence APIs, not payment authority.
/// - These endpoints never create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, consume gate authorization, or mutate provider outcome truth.
/// - Correlation metadata is required for run creation.
/// </summary>
public static class ReconciliationRunItemEndpoints
{
    private const string RunCreatorPolicy = "ReconciliationRunCreator";
    private const string RunViewerPolicy = "ReconciliationRunViewer";
    private const string ItemViewerPolicy = "ReconciliationItemViewer";

    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ReconciliationRunItems");

    /// <summary>
    /// Maps Central PMS reconciliation run and item endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapReconciliationRunItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/reconciliation")
            .WithTags("OpsReconciliation");

        group.MapPost("/runs", CreateRunAsync)
            .WithName("CreateReconciliationRun")
            .WithMetadata(new ReconciliationPolicyMetadata(RunCreatorPolicy))
            .Produces<CreateReconciliationRunResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/runs/{reconciliationRunId:guid}", ReadRunAsync)
            .WithName("GetReconciliationRun")
            .WithMetadata(new ReconciliationPolicyMetadata(RunViewerPolicy))
            .Produces<ReconciliationRunDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/runs/{reconciliationRunId:guid}/items", ListRunItemsAsync)
            .WithName("ListReconciliationRunItems")
            .WithMetadata(new ReconciliationPolicyMetadata(ItemViewerPolicy))
            .Produces<ReconciliationItemsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/items/{reconciliationItemId:guid}", ReadItemAsync)
            .WithName("GetReconciliationItem")
            .WithMetadata(new ReconciliationPolicyMetadata(ItemViewerPolicy))
            .Produces<ReconciliationItemSummary>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> CreateRunAsync(
        CreateReconciliationRunRequest request,
        HttpRequest httpRequest,
        IReconciliationRunItemService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP CreateReconciliationRun", httpRequest, Guid.Empty);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationRunItemEndpoints");

        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.CreateRunAsync(
                new CreateReconciliationRunCommand(
                    request.RunType,
                    request.ScopeType,
                    request.RunCode,
                    string.IsNullOrWhiteSpace(request.RunStatus) ? "STARTED" : request.RunStatus,
                    request.SiteGroupId,
                    request.SiteId,
                    request.IncidentRecordId,
                    request.PaymentRailId,
                    request.VendorSystemId,
                    request.SourceBatchRef,
                    request.WindowStartAt,
                    request.WindowEndAt,
                    request.GenerateItems,
                    request.ActorUserId,
                    request.ServiceIdentityId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("reconciliation_run_id", result.ReconciliationRunId);
            activity?.SetTag("reconciliation_run_type", result.RunType);
            activity?.SetTag("reconciliation_scope_type", result.ScopeType);

            logger.LogInformation(
                "Reconciliation run created. reconciliation_run_id={ReconciliationRunId} run_code={RunCode} run_type={RunType} scope_type={ScopeType}",
                result.ReconciliationRunId,
                result.RunCode,
                result.RunType,
                result.ScopeType);

            return Results.Created(
                $"/v1/ops/reconciliation/runs/{result.ReconciliationRunId}",
                new CreateReconciliationRunResponse(
                    result.ReconciliationRunId,
                    result.RunCode,
                    result.RunType,
                    result.RunStatus,
                    result.ScopeType,
                    result.ItemCount,
                    result.ItemGenerationPerformed,
                    result.ItemGenerationMessage,
                    result.CorrelationId));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ReadRunAsync(
        Guid reconciliationRunId,
        IReconciliationRunItemService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetReconciliationRun", httpRequest, reconciliationRunId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationRunItemEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var record = await service.ReadRunAsync(new ReadReconciliationRunQuery(reconciliationRunId), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(record));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ListRunItemsAsync(
        Guid reconciliationRunId,
        int? limit,
        IReconciliationRunItemService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP ListReconciliationRunItems", httpRequest, reconciliationRunId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationRunItemEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var records = await service.ListRunItemsAsync(
                new ListReconciliationRunItemsQuery(reconciliationRunId, limit ?? 50),
                cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(new ReconciliationItemsResponse(records.Select(ToContract).ToArray()));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ReadItemAsync(
        Guid reconciliationItemId,
        IReconciliationRunItemService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetReconciliationItem", httpRequest, reconciliationItemId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ReconciliationRunItemEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var record = await service.ReadItemAsync(new ReadReconciliationItemQuery(reconciliationItemId), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(record));
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
            ArgumentException ex => Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId, retryable: false)),
            ReconciliationRunNotFoundException ex => Results.NotFound(BuildError("RECONCILIATION_RUN_NOT_FOUND", ex.Message, correlationId, retryable: false)),
            ReconciliationItemNotFoundException ex => Results.NotFound(BuildError("RECONCILIATION_ITEM_NOT_FOUND", ex.Message, correlationId, retryable: false)),
            ReconciliationRunItemRejectedException ex when IsMissingReference(ex.ErrorCode) => Results.NotFound(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false)),
            ReconciliationRunItemRejectedException ex when ex.ErrorCode.Contains("ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase) => Results.Conflict(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false)),
            ReconciliationRunItemRejectedException ex => Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId, retryable: false)),
            _ => Unexpected(exception, correlationId, logger)
        };
    }

    private static bool IsMissingReference(string errorCode) =>
        errorCode.EndsWith("_NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    private static IResult Unexpected(Exception exception, Guid correlationId, ILogger logger)
    {
        logger.LogError(exception, "Unexpected reconciliation run/item API failure.");
        return Results.Json(
            BuildError(
                "RECONCILIATION_RUN_ITEM_INTERNAL_ERROR",
                "An unexpected error occurred while processing the reconciliation run/item request.",
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

    private static ReconciliationRunDetailResponse ToContract(ReconciliationRunDetailRecord record) =>
        new(
            record.ReconciliationRunId,
            record.RunCode,
            record.RunType,
            record.RunStatus,
            record.ScopeType,
            record.SiteGroupId,
            record.SiteId,
            record.IncidentRecordId,
            record.PaymentRailId,
            record.VendorSystemId,
            record.SourceBatchRef,
            record.WindowStartAt,
            record.WindowEndAt,
            record.StartedAt,
            record.CompletedAt,
            record.FailedAt,
            record.FailureReasonCode,
            record.ItemCount,
            record.MatchedCount,
            record.ExceptionCount,
            record.RejectedCount,
            record.DisputedCount,
            record.InitiatedByUserId,
            record.InitiatedByServiceIdentityId,
            record.CorrelationId);

    private static ReconciliationItemSummary ToContract(ReconciliationItemRecord record) =>
        new(
            record.ReconciliationItemId,
            record.ReconciliationRunId,
            record.MopsTransactionRecordId,
            record.ManualGateLogId,
            record.PaymentAttemptId,
            record.PaymentConfirmationId,
            record.ProviderOutcomeId,
            record.TargetEntityType,
            record.TargetEntityId,
            record.ComparisonBasis,
            record.ItemStatus,
            record.MatchStatus,
            record.ExpectedAmount,
            record.ActualAmount,
            record.CurrencyCode,
            record.VarianceAmount,
            record.ExceptionReasonCode,
            record.ResolvedAt,
            record.ResolvedByUserId,
            record.CreatedAt,
            record.UpdatedAt,
            record.CorrelationId);
}
