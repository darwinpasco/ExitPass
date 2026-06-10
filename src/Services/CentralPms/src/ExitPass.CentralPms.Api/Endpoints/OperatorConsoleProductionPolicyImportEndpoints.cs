using System.Diagnostics;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console production statutory discount policy import endpoints.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This endpoint is dry-run validation only.
/// - This endpoint never inserts, updates, deletes, seeds, activates, or approves statutory discount policy rows.
/// - This endpoint never mutates payment, provider, exit, gate, coupon, reconciliation, or payable-basis state.
/// </summary>
public static class OperatorConsoleProductionPolicyImportEndpoints
{
    private const int MaxCsvContentLength = 1_000_000;
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImport");

    /// <summary>
    /// Maps Operator Console production policy import endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleProductionPolicyImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapPost("/statutory-discounts/policies/import/dry-run", DryRunAsync)
            .WithName("DryRunOperatorConsoleProductionPolicyImport")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleProductionPolicyImportDryRunRequest>("application/json")
            .Produces<OperatorConsoleProductionPolicyImportDryRunResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Dry-run validate Operator Console statutory discount policy import")
            .WithDescription("Parses and validates production statutory discount policy candidate CSV content without importing, seeding, activating, approving, or writing any policy rows. Validation failures are returned as row findings with imported=false.");

        group.MapPost("/statutory-discounts/policies/import/reviews", SubmitReviewAsync)
            .WithName("SubmitOperatorConsoleProductionPolicyImportReview")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleProductionPolicyImportReviewSubmitRequest>("application/json")
            .Produces<OperatorConsoleProductionPolicyImportReviewResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Submit production policy import dry-run result for DB-backed review")
            .WithDescription("Persists a dry-run production policy import result into the DB-backed review queue. This endpoint does not import, seed, activate, or write production policy registry rows.");

        group.MapPost("/statutory-discounts/policies/import/reviews/{reviewId:guid}/decision", DecideReviewAsync)
            .WithName("DecideOperatorConsoleProductionPolicyImportReview")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleProductionPolicyImportReviewDecisionRequest>("application/json")
            .Produces<OperatorConsoleProductionPolicyImportReviewResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Record production policy import review decision")
            .WithDescription("Records checker approval, rejection, escalation, or requested changes in the DB-backed review queue. Approval stops at APPROVED_FOR_DB_REPO_ALIGNMENT and never imports or activates production policy rows.");

        return app;
    }

    private static async Task<IResult> DryRunAsync(
        OperatorConsoleProductionPolicyImportDryRunRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleProductionPolicyImportService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP DryRunOperatorConsoleProductionPolicyImport", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImportEndpoints");
        var correlationId = request.CorrelationId ?? ResolveCorrelationId(httpRequest) ?? Guid.NewGuid();

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("file_name", request.FileName);
        activity?.SetTag("dry_run_only", true);

        try
        {
            OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.SubmittedByOperatorId,
                fallbackCorrelationId: correlationId);

            if (string.IsNullOrWhiteSpace(request.CsvContent))
            {
                return Results.BadRequest(BuildError(
                    "INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_REQUEST",
                    "csvContent is required for dry-run validation.",
                    correlationId));
            }

            if (request.CsvContent.Length > MaxCsvContentLength)
            {
                return Results.BadRequest(BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_TOO_LARGE",
                    $"csvContent exceeds the dry-run limit of {MaxCsvContentLength} characters.",
                    correlationId));
            }

            var result = await service.DryRunAsync(
                new ProductionPolicyImportDryRunRequest(
                    request.CsvContent,
                    request.FileName,
                    correlationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("policy_import.total_rows", result.TotalRows);
            activity?.SetTag("policy_import.fail_count", result.FailCount);
            activity?.SetTag("policy_import.imported", false);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console production policy import dry run completed. total_rows={TotalRows} fail_count={FailCount} imported=false",
                result.TotalRows,
                result.FailCount);

            return Results.Ok(ToContract(result, correlationId));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_REQUEST",
                ex.Message,
                correlationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console production policy import dry run failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_FAILED",
                    "The Operator Console production policy import dry run could not be completed.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> SubmitReviewAsync(
        OperatorConsoleProductionPolicyImportReviewSubmitRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleProductionPolicyImportReviewService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP SubmitOperatorConsoleProductionPolicyImportReview", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImportEndpoints");
        var correlationId = request.CorrelationId ?? ResolveCorrelationId(httpRequest) ?? request.DryRunResult?.CorrelationId ?? Guid.NewGuid();

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("policy_import.imported", false);
        activity?.SetTag("policy_import.activation_blocked", true);

        try
        {
            if (request.DryRunResult is null)
            {
                return Results.BadRequest(BuildError(
                    "INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_SUBMIT_REQUEST",
                    "dryRunResult is required.",
                    correlationId));
            }

            var identity = OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.SubmittedByOperatorId,
                fallbackCorrelationId: correlationId);
            correlationId = identity.CorrelationId;

            var result = await service.SubmitForReviewAsync(
                new ProductionPolicyImportReviewSubmitRequest(
                    identity.UserId,
                    request.FileName,
                    ToApplication(request.DryRunResult),
                    correlationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("review_id", result.Submission.ReviewId);
            activity?.SetTag("review_status", result.Submission.Status.ToString());
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console production policy import review submitted. review_id={ReviewId} status={Status} imported=false activation_blocked=true",
                result.Submission.ReviewId,
                result.Submission.Status);

            return Results.Ok(ToContract(result, correlationId));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_SUBMIT_REQUEST",
                ex.Message,
                correlationId));
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.Conflict(BuildError(
                "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_SUBMIT_REJECTED",
                ex.Message,
                correlationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console production policy import review submission failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_SUBMIT_FAILED",
                    "The Operator Console production policy import review submission could not be completed.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DecideReviewAsync(
        Guid reviewId,
        OperatorConsoleProductionPolicyImportReviewDecisionRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleProductionPolicyImportReviewService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP DecideOperatorConsoleProductionPolicyImportReview", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImportEndpoints");
        var correlationId = request.CorrelationId ?? ResolveCorrelationId(httpRequest) ?? Guid.NewGuid();

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("review_id", reviewId);
        activity?.SetTag("policy_import.imported", false);
        activity?.SetTag("policy_import.activation_blocked", true);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.ReviewerOperatorId,
                fallbackCorrelationId: correlationId);
            correlationId = identity.CorrelationId;

            if (!Enum.TryParse<ProductionPolicyImportReviewDecisionAction>(request.Action, ignoreCase: false, out var action))
            {
                throw new ArgumentException("Unsupported production policy import review decision.", nameof(request.Action));
            }

            var result = await service.DecideAsync(
                new ProductionPolicyImportReviewDecisionRequest(
                    reviewId,
                    identity.UserId,
                    action,
                    request.Reason,
                    correlationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("review_status", result.Submission.Status.ToString());
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console production policy import review decision recorded. review_id={ReviewId} action={Action} status={Status} imported=false activation_blocked=true",
                result.Submission.ReviewId,
                action,
                result.Submission.Status);

            return Results.Ok(ToContract(result, correlationId));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_DECISION_REQUEST",
                ex.Message,
                correlationId));
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.Conflict(BuildError(
                "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_DECISION_REJECTED",
                ex.Message,
                correlationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console production policy import review decision failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_DECISION_FAILED",
                    "The Operator Console production policy import review decision could not be completed.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OperatorConsoleProductionPolicyImportDryRunResponse ToContract(
        ProductionPolicyImportDryRunResult result,
        Guid correlationId) =>
        new(
            Imported: false,
            ImportedRowCount: 0,
            DryRunOnly: true,
            Message: "Dry run completed. No policies were imported.",
            new OperatorConsoleProductionPolicyImportDryRunSummary(
                result.TotalRows,
                result.PassCount,
                result.WarnCount,
                result.FailCount,
                result.ImportableRows,
                result.ManualReviewRows,
                result.NotImportableRows,
                result.DryRunOnlyRows,
                result.DuplicateRows),
            result.Rows.Select(ToContract).ToArray(),
            correlationId);

    private static OperatorConsoleProductionPolicyImportDryRunRow ToContract(
        ProductionPolicyImportRowResult row) =>
        new(
            row.RowNumber,
            row.PolicyCode,
            row.EntitlementType,
            row.Decision.ToString(),
            row.Findings.Select(ToContract).ToArray());

    private static OperatorConsoleProductionPolicyImportDryRunFinding ToContract(
        ProductionPolicyImportFinding finding) =>
        new(
            finding.Severity.ToString(),
            ToFindingCode(finding),
            finding.Message,
            finding.Field);

    private static OperatorConsoleProductionPolicyImportReviewResponse ToContract(
        ProductionPolicyImportReviewSubmitResult result,
        Guid correlationId) =>
        new(
            Imported: false,
            ProductionPolicyActivationBlocked: true,
            result.Message,
            ToContract(result.Submission),
            result.Findings.Select(ToContract).ToArray(),
            correlationId);

    private static OperatorConsoleProductionPolicyImportReviewResponse ToContract(
        ProductionPolicyImportReviewDecisionResult result,
        Guid correlationId) =>
        new(
            Imported: false,
            ProductionPolicyActivationBlocked: true,
            result.Message,
            ToContract(result.Submission),
            result.Findings.Select(ToContract).ToArray(),
            correlationId);

    private static OperatorConsoleProductionPolicyImportReviewSubmission ToContract(
        ProductionPolicyImportReviewSubmission submission) =>
        new(
            submission.ReviewId,
            submission.MakerOperatorId,
            submission.FileName,
            submission.Status.ToString(),
            new OperatorConsoleProductionPolicyImportDryRunSummary(
                submission.DryRunResult.TotalRows,
                submission.DryRunResult.PassCount,
                submission.DryRunResult.WarnCount,
                submission.DryRunResult.FailCount,
                submission.DryRunResult.ImportableRows,
                submission.DryRunResult.ManualReviewRows,
                submission.DryRunResult.NotImportableRows,
                submission.DryRunResult.DryRunOnlyRows,
                submission.DryRunResult.DuplicateRows),
            submission.ReviewerDecisions.Select(ToContract).ToArray(),
            submission.History.Select(ToContract).ToArray(),
            submission.CreatedAt,
            submission.UpdatedAt);

    private static OperatorConsoleProductionPolicyImportReviewDecision ToContract(
        ProductionPolicyImportReviewDecision decision) =>
        new(
            decision.ReviewerRole.ToString(),
            decision.Action.ToString(),
            decision.ReviewerOperatorId,
            decision.Reason,
            decision.DecidedAt,
            decision.CorrelationId);

    private static OperatorConsoleProductionPolicyImportReviewHistoryEntry ToContract(
        ProductionPolicyImportReviewHistoryEntry history) =>
        new(
            history.Action.ToString(),
            history.Status.ToString(),
            history.ActorOperatorId,
            history.ReviewerRole?.ToString(),
            history.Reason,
            history.OccurredAt,
            history.CorrelationId);

    private static OperatorConsoleProductionPolicyImportReviewFinding ToContract(
        ProductionPolicyImportReviewFinding finding) =>
        new(
            finding.Severity.ToString(),
            finding.Message,
            finding.Field);

    private static ProductionPolicyImportDryRunResult ToApplication(
        OperatorConsoleProductionPolicyImportDryRunResponse response) =>
        new(
            IsDryRun: response.DryRunOnly,
            PoliciesImported: response.Imported,
            TotalRows: response.Summary.TotalRows,
            ImportableRows: response.Summary.ImportableCount,
            ManualReviewRows: response.Summary.ManualReviewCount,
            NotImportableRows: response.Summary.NotImportableCount,
            DryRunOnlyRows: response.Summary.DryRunOnlyCount,
            DuplicateRows: response.Summary.DuplicateCount,
            PassCount: response.Summary.PassCount,
            WarnCount: response.Summary.WarnCount,
            FailCount: response.Summary.FailCount,
            Rows: response.Rows.Select(ToApplication).ToArray(),
            Findings: Array.Empty<ProductionPolicyImportFinding>(),
            response.CorrelationId);

    private static ProductionPolicyImportRowResult ToApplication(
        OperatorConsoleProductionPolicyImportDryRunRow row) =>
        new(
            row.RowNumber,
            row.PolicyCode,
            row.EntitlementType,
            Enum.Parse<ProductionPolicyImportRowDecision>(row.Decision, ignoreCase: false),
            row.Findings.Select(ToApplication).ToArray());

    private static ProductionPolicyImportFinding ToApplication(
        OperatorConsoleProductionPolicyImportDryRunFinding finding) =>
        new(
            Enum.Parse<ProductionPolicyImportFindingSeverity>(finding.Severity, ignoreCase: false),
            finding.Message,
            RowNumber: null,
            finding.FieldName);

    private static string ToFindingCode(ProductionPolicyImportFinding finding)
    {
        var source = finding.Field ?? finding.Message;
        var chars = source
            .Select(static c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_')
            .ToArray();
        var code = new string(chars).Trim('_');
        while (code.Contains("__", StringComparison.Ordinal))
        {
            code = code.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(code)
            ? finding.Severity.ToString()
            : code.Length > 96 ? code[..96] : code;
    }

    private static Guid? ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var value) &&
        Guid.TryParse(value.ToString(), out var correlationId)
            ? correlationId
            : null;

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };
}
