using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Reconciliation;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator endpoints for importing and reading MoPS continuity transaction evidence.
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
/// - MoPS records are continuity evidence and reconciliation inputs only.
/// - These endpoints never create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, consume gate authorization, or mutate provider outcome truth.
/// - Correlation metadata is required for import operations.
/// </summary>
public static class MopsTransactionEndpoints
{
    private const string ImporterPolicy = "MopsTransactionImporter";
    private const string ViewerPolicy = "MopsTransactionViewer";

    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.MopsTransactions");

    /// <summary>
    /// Maps Central PMS MoPS transaction endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapMopsTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/mops-transactions")
            .WithTags("OpsMopsTransactions");

        group.MapPost("/import", ImportAsync)
            .WithName("ImportMopsTransaction")
            .WithMetadata(new ReconciliationPolicyMetadata(ImporterPolicy))
            .Produces<ImportMopsTransactionResponse>(StatusCodes.Status201Created)
            .Produces<ImportMopsTransactionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/", ListAsync)
            .WithName("ListMopsTransactions")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<MopsTransactionsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{mopsTransactionRecordId:guid}", ReadAsync)
            .WithName("GetMopsTransaction")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<MopsTransactionSummary>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> ImportAsync(
        ImportMopsTransactionRequest request,
        HttpRequest httpRequest,
        IMopsTransactionService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP ImportMopsTransaction", httpRequest, request.SiteId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.MopsTransactionEndpoints");

        if (!TryGetCorrelationId(httpRequest, out var correlationError, out var correlationId))
        {
            return correlationError;
        }

        try
        {
            var result = await service.ImportAsync(
                new ImportMopsTransactionCommand(
                    request.SiteId,
                    request.SiteGroupId,
                    request.PaymentRailId,
                    request.VendorSystemId,
                    request.ParkingSessionId,
                    request.LaneId,
                    request.SourceSystemCode,
                    request.SourceTransactionRef,
                    request.SourceBatchRef,
                    request.CollectionReference,
                    request.CurrencyCode,
                    request.Amount,
                    request.PaymentMethodLabel,
                    request.ContinuityReasonCode,
                    request.CapturedAt,
                    request.EvidenceRef,
                    request.EvidenceHash,
                    request.ActorUserId,
                    request.ImportedByServiceIdentityId,
                    correlationId),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("mops_transaction_record_id", result.MopsTransactionRecordId);
            activity?.SetTag("reconciliation_run_id", result.ReconciliationRunId);
            activity?.SetTag("reconciliation_item_id", result.ReconciliationItemId);
            activity?.SetTag("mops_import_duplicate", result.WasDuplicate);

            logger.LogInformation(
                "MoPS transaction import accepted. mops_transaction_record_id={MopsTransactionRecordId} reconciliation_run_id={ReconciliationRunId} reconciliation_item_id={ReconciliationItemId} duplicate={WasDuplicate}",
                result.MopsTransactionRecordId,
                result.ReconciliationRunId,
                result.ReconciliationItemId,
                result.WasDuplicate);

            var response = new ImportMopsTransactionResponse(
                result.MopsTransactionRecordId,
                result.ReconciliationRunId,
                result.ReconciliationItemId,
                result.RecordStatus,
                result.RunCode,
                result.WasDuplicate,
                result.CorrelationId);

            return result.WasDuplicate
                ? Results.Ok(response)
                : Results.Created($"/v1/ops/mops-transactions/{result.MopsTransactionRecordId}", response);
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> ListAsync(
        int? limit,
        Guid? siteId,
        string? sourceSystemCode,
        IMopsTransactionService service,
        CancellationToken cancellationToken)
    {
        var records = await service.ListAsync(
            new ListMopsTransactionsQuery(limit ?? 20, siteId, sourceSystemCode),
            cancellationToken);

        return Results.Ok(new MopsTransactionsResponse(records.Select(ToContract).ToArray()));
    }

    private static async Task<IResult> ReadAsync(
        Guid mopsTransactionRecordId,
        IMopsTransactionService service,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetMopsTransaction", httpRequest, mopsTransactionRecordId);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.MopsTransactionEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var record = await service.ReadAsync(
                new ReadMopsTransactionQuery(mopsTransactionRecordId),
                cancellationToken);

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
        activity?.SetTag("mops_entity_id", entityId);
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
            MopsTransactionNotFoundException ex => Results.NotFound(BuildError(
                "MOPS_TRANSACTION_NOT_FOUND",
                ex.Message,
                correlationId,
                retryable: false)),
            MopsImportRejectedException ex when IsMissingReference(ex.ErrorCode) => Results.NotFound(BuildError(
                ex.ErrorCode,
                ex.Message,
                correlationId,
                retryable: false)),
            MopsImportRejectedException ex when ex.ErrorCode.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase) => Results.Conflict(BuildError(
                ex.ErrorCode,
                ex.Message,
                correlationId,
                retryable: false)),
            MopsImportRejectedException ex => Results.BadRequest(BuildError(
                ex.ErrorCode,
                ex.Message,
                correlationId,
                retryable: false)),
            _ => Unexpected(exception, correlationId, logger)
        };
    }

    private static bool IsMissingReference(string errorCode) =>
        string.Equals(errorCode, "SITE_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, "PAYMENT_RAIL_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, "VENDOR_SYSTEM_NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    private static IResult Unexpected(Exception exception, Guid correlationId, ILogger logger)
    {
        logger.LogError(exception, "Unexpected MoPS transaction API failure.");

        return Results.Json(
            BuildError(
                "MOPS_TRANSACTION_INTERNAL_ERROR",
                "An unexpected error occurred while processing the MoPS transaction request.",
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

    private static MopsTransactionSummary ToContract(MopsTransactionRecord record) =>
        new(
            record.MopsTransactionRecordId,
            record.ReconciliationRunId,
            record.ReconciliationItemId,
            record.SiteId,
            record.SiteGroupId,
            record.PaymentRailId,
            record.VendorSystemId,
            record.ParkingSessionId,
            record.LaneId,
            record.SourceSystemCode,
            record.SourceTransactionRef,
            record.SourceBatchRef,
            record.CollectionReference,
            record.CurrencyCode,
            record.Amount,
            record.PaymentMethodLabel,
            record.ContinuityReasonCode,
            record.RecordStatus,
            record.CapturedAt,
            record.ImportedAt,
            record.EvidenceRef,
            record.CorrelationId);
}
