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
