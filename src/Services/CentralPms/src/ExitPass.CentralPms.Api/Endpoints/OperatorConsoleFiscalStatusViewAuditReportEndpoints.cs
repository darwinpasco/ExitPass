using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console read-only fiscal status view-audit report endpoint.
/// </summary>
public static class OperatorConsoleFiscalStatusViewAuditReportEndpoints
{
    private const string StatusReadPolicy = "FiscalIssuanceStatusRead";
    private const string WorkflowCode = OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow;
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleFiscalStatusViewAuditReport");

    /// <summary>
    /// Maps Operator Console fiscal status view-audit report endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleFiscalStatusViewAuditReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapGet("/audit/fiscal-status-views", ListAsync)
            .WithName("ListOperatorConsoleFiscalStatusViewAuditReport")
            .WithTags("OperatorConsole")
            .WithMetadata(new ReconciliationPolicyMetadata(StatusReadPolicy))
            .Produces<OperatorConsoleFiscalStatusViewAuditReportResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("List Operator Console fiscal status view-audit report rows")
            .WithDescription("Returns read-only safe metadata for VIEW_FISCAL_ISSUANCE_STATUS action-log entries. This endpoint does not return raw payloads and does not call POS Server or mutate fiscal, payment, exit, gate, refund, reversal, retry, readback, writeback, or document-rendering state.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? siteId,
        Guid? siteGroupId,
        Guid? operatorUserId,
        Guid? fiscalIssuanceReferenceId,
        string? resultClass,
        Guid? correlationId,
        int? limit,
        int? offset,
        HttpRequest httpRequest,
        IOperatorConsoleFiscalStatusViewAuditReportService service,
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = ResolveRequestCorrelationId(httpRequest);
        using var activity = ActivitySource.StartActivity("HTTP ListOperatorConsoleFiscalStatusViewAuditReport", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleFiscalStatusViewAuditReportEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", effectiveCorrelationId);
        activity?.SetTag("result_class", resultClass);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);
            effectiveCorrelationId = identity.CorrelationId;

            var access = await EvaluateAndPersistAccessAsync(
                identity with
                {
                    SiteId = siteId ?? identity.SiteId,
                    SiteGroupId = siteGroupId ?? identity.SiteGroupId
                },
                accessEvaluationService,
                accessEvaluationWriter,
                httpRequest);

            if (!access.Allowed)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.Json(
                    BuildError(
                        "OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_AUDIT_REPORT_ACCESS_DENIED",
                        "Access denied for the fiscal status view-audit report.",
                        effectiveCorrelationId),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await service.ListAsync(
                new OperatorConsoleFiscalStatusViewAuditReportQuery(
                    from,
                    to,
                    siteId ?? access.SiteContext.SiteId,
                    siteGroupId ?? access.SiteContext.SiteGroupId,
                    operatorUserId,
                    fiscalIssuanceReferenceId,
                    resultClass,
                    correlationId,
                    limit.GetValueOrDefault(25),
                    offset.GetValueOrDefault(0),
                    effectiveCorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("report_count", result.Items.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_AUDIT_REPORT_REQUEST",
                ex.Message,
                effectiveCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console fiscal status view-audit report read failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_AUDIT_REPORT_READ_FAILED",
                    "The Operator Console fiscal status view-audit report could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<OperatorConsoleAccessEvaluationResult> EvaluateAndPersistAccessAsync(
        OperatorConsoleIdentityContext identity,
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        HttpRequest httpRequest)
    {
        var evaluation = await accessEvaluationService.EvaluateAsync(
            new OperatorConsoleAccessEvaluationCommand(
                identity.UserId,
                identity.OperatorDeviceBindingId,
                identity.SiteId,
                identity.SiteGroupId,
                identity.OperatorShiftId,
                WorkflowCode,
                OperatorConsoleActionCodes.ViewFiscalStatusViewAuditReport,
                ParkingSessionId: null,
                EvidenceAccessIntent: null,
                $"operator-console-fiscal-status-view-audit-report-{identity.CorrelationId:N}",
                identity.CorrelationId),
            httpRequest.HttpContext.RequestAborted);

        return await accessEvaluationWriter.PersistAsync(evaluation, httpRequest.HttpContext.RequestAborted);
    }

    private static OperatorConsoleFiscalStatusViewAuditReportResponse ToContract(
        OperatorConsoleFiscalStatusViewAuditReportResult result) =>
        new(
            result.Items.Select(item => new OperatorConsoleFiscalStatusViewAuditReportItem(
                item.ActionLogEntryId,
                item.ActionTimestamp,
                item.ActionCode,
                item.ResultClass,
                item.OperatorUserId,
                item.SiteId,
                item.SiteGroupId,
                item.FiscalIssuanceReferenceId,
                item.CorrelationId,
                item.SafeDenialOrErrorPosture,
                item.SourceModule)).ToArray(),
            result.TotalCount,
            result.Limit,
            result.Offset,
            result.CorrelationId);

    private static Guid ResolveRequestCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerCorrelationId) &&
            headerCorrelationId != Guid.Empty)
        {
            return headerCorrelationId;
        }

        return Guid.NewGuid();
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };
}
