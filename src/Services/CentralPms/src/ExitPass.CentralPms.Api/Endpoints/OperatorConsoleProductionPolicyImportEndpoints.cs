using System.Diagnostics;
using System.Security.Claims;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Api.Security;
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
    private const string SubmitPolicy = "OperatorConsolePolicyImportReviewSubmit";
    private const string ViewerPolicy = "OperatorConsolePolicyImportReviewViewer";
    private const string DecisionPolicy = "OperatorConsolePolicyImportReviewDecision";
    private const string PermissionSubmit = "operator-console.policy-import-review.submit";
    private const string PermissionViewOwn = "operator-console.policy-import-review.view-own";
    private const string PermissionReview = "operator-console.policy-import-review.review";
    private const string PermissionManage = "operator-console.policy-import-review.manage";
    private const string PermissionApproveLegal = "operator-console.policy-import-review.approve.legal";
    private const string PermissionApproveOps = "operator-console.policy-import-review.approve.ops";
    private const string PermissionApproveQa = "operator-console.policy-import-review.approve.qa";
    private const string PermissionApproveDb = "operator-console.policy-import-review.approve.db";
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
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(SubmitPolicy))
            .WithSummary("Submit production policy import dry-run result for DB-backed review")
            .WithDescription("Persists a dry-run production policy import result into the DB-backed review queue. This endpoint does not import, seed, activate, or write production policy registry rows.");

        group.MapGet("/statutory-discounts/policies/import/reviews", ListReviewsAsync)
            .WithName("ListOperatorConsoleProductionPolicyImportReviews")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleProductionPolicyImportReviewListResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .WithSummary("List DB-backed production policy import review submissions")
            .WithDescription("Retrieves persisted production policy import review queue submissions. This endpoint does not import, seed, activate, or write production policy registry rows.");

        group.MapGet("/statutory-discounts/policies/import/reviews/{reviewId:guid}", GetReviewAsync)
            .WithName("GetOperatorConsoleProductionPolicyImportReview")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleProductionPolicyImportReviewResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .WithSummary("Get one DB-backed production policy import review submission")
            .WithDescription("Retrieves one persisted production policy import review submission by reviewId. This endpoint does not import, seed, activate, or write production policy registry rows.");

        group.MapPost("/statutory-discounts/policies/import/reviews/{reviewId:guid}/decision", DecideReviewAsync)
            .WithName("DecideOperatorConsoleProductionPolicyImportReview")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleProductionPolicyImportReviewDecisionRequest>("application/json")
            .Produces<OperatorConsoleProductionPolicyImportReviewResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(DecisionPolicy))
            .WithSummary("Record production policy import review decision")
            .WithDescription("Records checker approval, rejection, escalation, or requested changes in the DB-backed review queue. Approval stops at APPROVED_FOR_DB_REPO_ALIGNMENT and never imports or activates production policy rows.");

        return app;
    }

    private static async Task<IResult> ListReviewsAsync(
        HttpRequest httpRequest,
        IOperatorConsoleProductionPolicyImportReviewService service,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory,
        string? status = null,
        Guid? makerOperatorId = null,
        Guid? reviewerOperatorId = null,
        string? reviewerRole = null,
        DateTimeOffset? createdFrom = null,
        DateTimeOffset? createdTo = null,
        int? limit = null,
        int? offset = null,
        Guid? correlationId = null)
    {
        using var activity = ActivitySource.StartActivity("HTTP ListOperatorConsoleProductionPolicyImportReviews", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImportEndpoints");
        var resolvedCorrelationId = correlationId ?? ResolveCorrelationId(httpRequest) ?? Guid.NewGuid();

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", resolvedCorrelationId);
        activity?.SetTag("policy_import.imported", false);
        activity?.SetTag("policy_import.activation_blocked", true);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: resolvedCorrelationId);
            resolvedCorrelationId = identity.CorrelationId;
            var access = await ResolveReviewAccessAsync(httpRequest, identity.UserId, rbacRepository, httpRequest.HttpContext.RequestAborted);

            if (!CanListReviews(access))
            {
                await AuditReviewActionAsync(
                    rbacRepository,
                    logger,
                    "OperatorConsoleProductionPolicyImportReviewAccessDenied",
                    "DENIED",
                    "REVIEW_LIST_FORBIDDEN",
                    null,
                    identity.UserId,
                    resolvedCorrelationId,
                    "Operator Console production policy import review list access denied.",
                    httpRequest.HttpContext.RequestAborted);
                return Forbidden("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_FORBIDDEN", "The operator is not authorized to list production policy import reviews.", resolvedCorrelationId);
            }

            var effectiveMakerOperatorId = CanReviewAny(access)
                ? makerOperatorId
                : identity.UserId;
            var result = await service.ListAsync(
                new ProductionPolicyImportReviewQuery(
                    ParseNullableEnum<ProductionPolicyImportReviewSubmissionStatus>(status, nameof(status)),
                    effectiveMakerOperatorId,
                    reviewerOperatorId,
                    ParseNullableEnum<ProductionPolicyImportReviewerRole>(reviewerRole, nameof(reviewerRole)),
                    createdFrom,
                    createdTo,
                    limit ?? 50,
                    offset ?? 0),
                resolvedCorrelationId,
                httpRequest.HttpContext.RequestAborted);
            var visibleItems = result.Items
                .Where(submission => CanViewSubmission(access, identity.UserId, submission))
                .ToArray();
            result = result with
            {
                Items = visibleItems,
                TotalCount = visibleItems.Length
            };

            activity?.SetTag("review_count", result.Items.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console production policy import reviews listed. count={Count} imported=false activation_blocked=true",
                result.Items.Count);

            await AuditReviewActionAsync(
                rbacRepository,
                logger,
                "OperatorConsoleProductionPolicyImportReviewListed",
                "SUCCESS",
                "REVIEW_LISTED",
                null,
                identity.UserId,
                resolvedCorrelationId,
                $"Operator Console production policy import reviews listed. count={result.Items.Count}.",
                httpRequest.HttpContext.RequestAborted);

            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_LIST_REQUEST",
                ex.Message,
                resolvedCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console production policy import review list failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_LIST_FAILED",
                    "The Operator Console production policy import review list could not be loaded.",
                    resolvedCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetReviewAsync(
        Guid reviewId,
        HttpRequest httpRequest,
        IOperatorConsoleProductionPolicyImportReviewService service,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory,
        Guid? correlationId = null)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetOperatorConsoleProductionPolicyImportReview", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImportEndpoints");
        var resolvedCorrelationId = correlationId ?? ResolveCorrelationId(httpRequest) ?? Guid.NewGuid();

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", resolvedCorrelationId);
        activity?.SetTag("review_id", reviewId);
        activity?.SetTag("policy_import.imported", false);
        activity?.SetTag("policy_import.activation_blocked", true);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: resolvedCorrelationId);
            resolvedCorrelationId = identity.CorrelationId;
            var submission = await service.GetAsync(reviewId, httpRequest.HttpContext.RequestAborted);
            if (submission is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Review submission was not found.");
                return Results.NotFound(BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_NOT_FOUND",
                    "Review submission was not found.",
                    resolvedCorrelationId));
            }

            var access = await ResolveReviewAccessAsync(httpRequest, identity.UserId, rbacRepository, httpRequest.HttpContext.RequestAborted);
            if (!CanViewSubmission(access, identity.UserId, submission))
            {
                await AuditReviewActionAsync(
                    rbacRepository,
                    logger,
                    "OperatorConsoleProductionPolicyImportReviewAccessDenied",
                    "DENIED",
                    "REVIEW_DETAIL_FORBIDDEN",
                    submission.ReviewId,
                    identity.UserId,
                    resolvedCorrelationId,
                    $"Operator Console production policy import review detail access denied. review_id={submission.ReviewId}.",
                    httpRequest.HttpContext.RequestAborted);
                return Forbidden("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_FORBIDDEN", "The operator is not authorized to view this production policy import review.", resolvedCorrelationId);
            }

            activity?.SetTag("review_status", submission.Status.ToString());
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console production policy import review loaded. review_id={ReviewId} status={Status} imported=false activation_blocked=true",
                submission.ReviewId,
                submission.Status);

            await AuditReviewActionAsync(
                rbacRepository,
                logger,
                "OperatorConsoleProductionPolicyImportReviewDetailViewed",
                "SUCCESS",
                "REVIEW_DETAIL_VIEWED",
                submission.ReviewId,
                identity.UserId,
                resolvedCorrelationId,
                $"Operator Console production policy import review detail viewed. review_id={submission.ReviewId}.",
                httpRequest.HttpContext.RequestAborted);

            return Results.Ok(ToContract(submission, resolvedCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console production policy import review detail failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_DETAIL_FAILED",
                    "The Operator Console production policy import review detail could not be loaded.",
                    resolvedCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
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
        ICentralPmsRbacRepository rbacRepository,
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
            var access = await ResolveReviewAccessAsync(httpRequest, identity.UserId, rbacRepository, httpRequest.HttpContext.RequestAborted);
            if (!access.CanSubmit)
            {
                await AuditReviewActionAsync(
                    rbacRepository,
                    logger,
                    "OperatorConsoleProductionPolicyImportReviewAccessDenied",
                    "DENIED",
                    "REVIEW_SUBMIT_FORBIDDEN",
                    null,
                    identity.UserId,
                    correlationId,
                    "Operator Console production policy import review submit access denied.",
                    httpRequest.HttpContext.RequestAborted);
                return Forbidden("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_FORBIDDEN", "The operator is not authorized to submit production policy import reviews.", correlationId);
            }

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

            await AuditReviewActionAsync(
                rbacRepository,
                logger,
                "OperatorConsoleProductionPolicyImportReviewSubmitted",
                "SUCCESS",
                "REVIEW_SUBMITTED",
                result.Submission.ReviewId,
                identity.UserId,
                correlationId,
                $"Operator Console production policy import review submitted. review_id={result.Submission.ReviewId}.",
                httpRequest.HttpContext.RequestAborted);

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
        ICentralPmsRbacRepository rbacRepository,
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

            var submission = await service.GetAsync(reviewId, httpRequest.HttpContext.RequestAborted)
                ?? throw new ArgumentException("Review submission was not found.", nameof(reviewId));
            var access = await ResolveReviewAccessAsync(httpRequest, identity.UserId, rbacRepository, httpRequest.HttpContext.RequestAborted);

            if (submission.MakerOperatorId == identity.UserId)
            {
                await AuditReviewActionAsync(
                    rbacRepository,
                    logger,
                    "OperatorConsoleProductionPolicyImportReviewSelfDecisionBlocked",
                    "REJECTED",
                    "MAKER_CHECKER_SELF_DECISION_BLOCKED",
                    submission.ReviewId,
                    identity.UserId,
                    correlationId,
                    $"Maker/checker self-decision blocked. review_id={submission.ReviewId}. action={action}.",
                    httpRequest.HttpContext.RequestAborted);
                return Results.Conflict(BuildError(
                    "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_SELF_DECISION_BLOCKED",
                    "Maker cannot decide their own production policy import review submission.",
                    correlationId));
            }

            if (!CanDecideReview(access, action))
            {
                await AuditReviewActionAsync(
                    rbacRepository,
                    logger,
                    "OperatorConsoleProductionPolicyImportReviewAccessDenied",
                    "DENIED",
                    "REVIEW_DECISION_FORBIDDEN",
                    submission.ReviewId,
                    identity.UserId,
                    correlationId,
                    $"Operator Console production policy import review decision access denied. review_id={submission.ReviewId}. action={action}.",
                    httpRequest.HttpContext.RequestAborted);
                return Forbidden("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_DECISION_FORBIDDEN", "The operator is not authorized to record this production policy import review decision.", correlationId);
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

            await AuditReviewActionAsync(
                rbacRepository,
                logger,
                "OperatorConsoleProductionPolicyImportReviewDecisionRecorded",
                "SUCCESS",
                "REVIEW_DECISION_RECORDED",
                result.Submission.ReviewId,
                identity.UserId,
                correlationId,
                $"Operator Console production policy import review decision recorded. review_id={result.Submission.ReviewId}. action={action}.",
                httpRequest.HttpContext.RequestAborted);

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

    private static OperatorConsoleProductionPolicyImportReviewListResponse ToContract(
        ProductionPolicyImportReviewListResult result) =>
        new(
            Imported: false,
            ProductionPolicyActivationBlocked: true,
            result.Items.Select(submission => new OperatorConsoleProductionPolicyImportReviewListItem(
                Imported: false,
                ProductionPolicyActivationBlocked: true,
                ToContract(submission),
                submission.Findings.Select(ToContract).ToArray())).ToArray(),
            result.TotalCount,
            result.Limit,
            result.Offset,
            result.CorrelationId);

    private static OperatorConsoleProductionPolicyImportReviewResponse ToContract(
        ProductionPolicyImportReviewSubmission submission,
        Guid correlationId) =>
        new(
            Imported: false,
            ProductionPolicyActivationBlocked: true,
            "Review submission loaded. No policies were imported or activated.",
            ToContract(submission),
            submission.Findings.Select(ToContract).ToArray(),
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

    private static async Task<ReviewAccess> ResolveReviewAccessAsync(
        HttpRequest request,
        Guid userId,
        ICentralPmsRbacRepository repository,
        CancellationToken cancellationToken)
    {
        var headerPermissions = ResolvePermissionHeader(request);
        var claimPermissions = ResolvePermissionClaims(request.HttpContext.User);

        async Task<bool> HasAsync(string permission)
        {
            if (headerPermissions.Contains(permission) || claimPermissions.Contains(permission))
            {
                return true;
            }

            return await repository.UserHasAnyPermissionAsync(userId, [permission], cancellationToken);
        }

        var manage = await HasAsync(PermissionManage);
        return new ReviewAccess(
            CanSubmit: manage || await HasAsync(PermissionSubmit),
            CanViewOwn: manage || await HasAsync(PermissionViewOwn) || await HasAsync(PermissionSubmit),
            CanReview: manage || await HasAsync(PermissionReview),
            CanManage: manage,
            CanApproveLegal: manage || await HasAsync(PermissionApproveLegal),
            CanApproveOps: manage || await HasAsync(PermissionApproveOps),
            CanApproveQa: manage || await HasAsync(PermissionApproveQa),
            CanApproveDb: manage || await HasAsync(PermissionApproveDb));
    }

    private static bool CanListReviews(ReviewAccess access) =>
        access.CanManage ||
        access.CanReview ||
        access.CanViewOwn ||
        access.CanApproveLegal ||
        access.CanApproveOps ||
        access.CanApproveQa ||
        access.CanApproveDb;

    private static bool CanReviewAny(ReviewAccess access) =>
        access.CanManage ||
        access.CanReview ||
        access.CanApproveLegal ||
        access.CanApproveOps ||
        access.CanApproveQa ||
        access.CanApproveDb;

    private static bool CanViewSubmission(
        ReviewAccess access,
        Guid userId,
        ProductionPolicyImportReviewSubmission submission)
    {
        if (access.CanManage || access.CanReview)
        {
            return true;
        }

        if (submission.MakerOperatorId == userId)
        {
            return access.CanViewOwn || access.CanSubmit;
        }

        if (submission.ReviewerDecisions.Any(decision => decision.ReviewerOperatorId == userId))
        {
            return true;
        }

        return submission.Status switch
        {
            ProductionPolicyImportReviewSubmissionStatus.LEGAL_REVIEW_PENDING => access.CanApproveLegal,
            ProductionPolicyImportReviewSubmissionStatus.OPS_REVIEW_PENDING => access.CanApproveOps,
            ProductionPolicyImportReviewSubmissionStatus.QA_REVIEW_PENDING => access.CanApproveQa,
            ProductionPolicyImportReviewSubmissionStatus.DB_REVIEW_PENDING => access.CanApproveDb,
            ProductionPolicyImportReviewSubmissionStatus.SUBMITTED_FOR_REVIEW => access.CanApproveLegal || access.CanApproveOps || access.CanApproveQa || access.CanApproveDb,
            _ => access.CanApproveLegal || access.CanApproveOps || access.CanApproveQa || access.CanApproveDb
        };
    }

    private static bool CanDecideReview(
        ReviewAccess access,
        ProductionPolicyImportReviewDecisionAction action)
    {
        if (access.CanManage)
        {
            return true;
        }

        return action switch
        {
            ProductionPolicyImportReviewDecisionAction.APPROVE_LEGAL => access.CanApproveLegal,
            ProductionPolicyImportReviewDecisionAction.APPROVE_OPS => access.CanApproveOps,
            ProductionPolicyImportReviewDecisionAction.APPROVE_QA => access.CanApproveQa,
            ProductionPolicyImportReviewDecisionAction.APPROVE_DB => access.CanApproveDb,
            ProductionPolicyImportReviewDecisionAction.REJECT => access.CanReview,
            ProductionPolicyImportReviewDecisionAction.REQUEST_CHANGES => access.CanReview,
            ProductionPolicyImportReviewDecisionAction.ESCALATE => access.CanReview,
            ProductionPolicyImportReviewDecisionAction.CANCEL => access.CanReview,
            ProductionPolicyImportReviewDecisionAction.MARK_SUPERSEDED => access.CanReview,
            ProductionPolicyImportReviewDecisionAction.SUBMIT_FOR_REVIEW => access.CanReview,
            _ => false
        };
    }

    private static HashSet<string> ResolvePermissionHeader(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, out var value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value.ToString()
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolvePermissionClaims(ClaimsPrincipal principal) =>
        principal.Claims
            .Where(claim =>
                string.Equals(claim.Type, CentralPmsRbacPolicyCatalog.PermissionClaimType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, "scope", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task AuditReviewActionAsync(
        ICentralPmsRbacRepository repository,
        ILogger logger,
        string eventType,
        string eventResult,
        string eventReasonCode,
        Guid? reviewId,
        Guid actorUserId,
        Guid correlationId,
        string summary,
        CancellationToken cancellationToken)
    {
        await repository.RecordAuditEventAsync(
            eventType,
            eventResult,
            eventReasonCode,
            "OperatorConsoleProductionPolicyImportReview",
            reviewId,
            actorUserId,
            null,
            correlationId,
            summary,
            cancellationToken);
    }

    private static IResult Forbidden(string errorCode, string message, Guid correlationId) =>
        Results.Json(
            BuildError(errorCode, message, correlationId),
            statusCode: StatusCodes.Status403Forbidden);

    private static TEnum? ParseNullableEnum<TEnum>(string? value, string parameterName)
        where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"{parameterName} has an unsupported value.", parameterName);
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };

    private sealed record ReviewAccess(
        bool CanSubmit,
        bool CanViewOwn,
        bool CanReview,
        bool CanManage,
        bool CanApproveLegal,
        bool CanApproveOps,
        bool CanApproveQa,
        bool CanApproveDb);
}
