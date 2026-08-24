using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class OperatorConsoleStatutoryEvidenceReviewEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryEvidenceReview");

    public static IEndpointRouteBuilder MapOperatorConsoleStatutoryEvidenceReviewEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console/statutory-discounts/reviews")
            .WithTags("OperatorConsole");

        group.MapGet("/{statutoryDiscountDecisionCommandId:guid}/evidence", ReadAsync)
            .WithName("ReadOperatorConsoleStatutoryEvidenceReview")
            .Produces<OperatorConsoleStatutoryEvidenceReviewResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(OperatorConsoleStatutoryEvidenceReviewConstants.Policy))
            .WithSummary("Read review-safe statutory evidence metadata")
            .WithDescription("Returns current review-safe evidence lifecycle metadata for an authorized Operator Console reviewer. It never returns evidence bytes, storage locators, checksums, provider authorization material, or mutation authority.");

        group.MapPost("/{statutoryDiscountDecisionCommandId:guid}/evidence/preview", PreviewAsync)
            .WithName("PreviewOperatorConsoleStatutoryEvidence")
            .Accepts<OperatorConsoleStatutoryEvidencePreviewRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg", additionalContentTypes: ["image/png"])
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status415UnsupportedMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(new ReconciliationPolicyMetadata(OperatorConsoleStatutoryEvidenceReviewConstants.Policy))
            .WithSummary("Stream reviewable statutory evidence inline")
            .WithDescription("Reauthorizes the Operator Console reviewer and streams one current reviewable JPEG or PNG through Central PMS. The evidence selector is carried in a CSRF-protected body and never exposed in the browser URL; provider URLs, object keys, checksums, credentials, and download authority are never returned.");

        return app;
    }

    private static async Task<IResult> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid? correlationId,
        HttpRequest request,
        IOperatorConsoleStatutoryEvidenceReviewService service,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ActivitySource.StartActivity("HTTP ReadOperatorConsoleStatutoryEvidenceReview", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryEvidenceReviewEndpoints");

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(request, fallbackCorrelationId: correlationId);
            effectiveCorrelationId = identity.CorrelationId;
            var result = await service.ReadAsync(
                    statutoryDiscountDecisionCommandId,
                    ToAccessContext(identity, $"operator-console-evidence-review-read-{statutoryDiscountDecisionCommandId:N}-{effectiveCorrelationId:N}"),
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result is null
                ? Results.NotFound(Error("OPERATOR_CONSOLE_STATUTORY_EVIDENCE_NOT_FOUND", "The statutory evidence review record was not found.", effectiveCorrelationId))
                : Results.Ok(ToContract(result));
        }
        catch (ArgumentException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            return Results.BadRequest(Error("INVALID_OPERATOR_CONSOLE_STATUTORY_EVIDENCE_REVIEW_REQUEST", exception.Message, effectiveCorrelationId));
        }
        catch (UnauthorizedAccessException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Results.Json(
                Error("OPERATOR_CONSOLE_STATUTORY_EVIDENCE_REVIEW_FORBIDDEN", "Operator Console statutory evidence review access was denied.", effectiveCorrelationId),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogError(
                "Operator Console statutory evidence metadata read failed with controlled exception type {ExceptionType} and correlation {CorrelationId}.",
                exception.GetType().Name,
                effectiveCorrelationId);
            return Results.Json(
                Error("OPERATOR_CONSOLE_STATUTORY_EVIDENCE_REVIEW_FAILED", "The statutory evidence review record could not be loaded.", effectiveCorrelationId, retryable: true),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> PreviewAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleStatutoryEvidencePreviewRequest body,
        Guid? correlationId,
        HttpRequest request,
        IOperatorConsoleStatutoryEvidenceReviewService service,
        IAntiforgery antiforgery,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ActivitySource.StartActivity("HTTP PreviewOperatorConsoleStatutoryEvidence", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryEvidenceReviewEndpoints");

        try
        {
            if (string.Equals(request.HttpContext.User.Identity?.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
            {
                await antiforgery.ValidateRequestAsync(request.HttpContext).ConfigureAwait(false);
            }
            var identity = OperatorConsoleIdentityContext.Resolve(request, fallbackCorrelationId: correlationId);
            effectiveCorrelationId = identity.CorrelationId;
            var result = await service.OpenPreviewAsync(
                    statutoryDiscountDecisionCommandId,
                    body.EvidenceItemReference,
                    ToAccessContext(identity, $"operator-console-evidence-preview-{statutoryDiscountDecisionCommandId:N}-{effectiveCorrelationId:N}"),
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result.Content is not null && result.AuditContext is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return new PreviewStreamResult(result.Content, result.AuditContext, service, logger);
            }

            activity?.SetStatus(ActivityStatusCode.Error);
            return PreviewError(result);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(Error("CSRF_VALIDATION_FAILED", "The secure evidence-preview request could not be validated.", effectiveCorrelationId));
        }
        catch (ArgumentException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            return Results.BadRequest(Error("INVALID_OPERATOR_CONSOLE_STATUTORY_EVIDENCE_PREVIEW_REQUEST", exception.Message, effectiveCorrelationId));
        }
        catch (UnauthorizedAccessException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return Results.Json(
                Error("OPERATOR_CONSOLE_STATUTORY_EVIDENCE_PREVIEW_FORBIDDEN", "Operator Console statutory evidence preview access was denied.", effectiveCorrelationId),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogError(
                "Operator Console statutory evidence preview failed before streaming with controlled exception type {ExceptionType} and correlation {CorrelationId}.",
                exception.GetType().Name,
                effectiveCorrelationId);
            return Results.Json(
                Error("OPERATOR_CONSOLE_STATUTORY_EVIDENCE_PREVIEW_FAILED", "The statutory evidence preview could not be opened.", effectiveCorrelationId, retryable: true),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult PreviewError(OperatorConsoleStatutoryEvidencePreviewResult result)
    {
        var code = result.ErrorCode ?? "OPERATOR_CONSOLE_STATUTORY_EVIDENCE_PREVIEW_NOT_ELIGIBLE";
        var message = code switch
        {
            "NOT_FOUND" => "The statutory evidence preview was not found.",
            "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA" => "The statutory evidence media type is not supported for inline preview.",
            "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE" => "The protected statutory evidence object is temporarily unavailable.",
            _ => "The statutory evidence item is not eligible for preview."
        };
        var status = code switch
        {
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA" => StatusCodes.Status415UnsupportedMediaType,
            "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status409Conflict
        };

        return Results.Json(Error(code, message, result.CorrelationId, result.Retryable), statusCode: status);
    }

    private static OperatorConsoleReviewAccessContext ToAccessContext(
        OperatorConsoleIdentityContext identity,
        string idempotencyKey) =>
        new(
            identity.UserId,
            identity.OperatorDeviceBindingId,
            identity.OperatorShiftId,
            identity.SiteId,
            identity.SiteGroupId,
            identity.CorrelationId,
            idempotencyKey);

    private static OperatorConsoleStatutoryEvidenceReviewResponse ToContract(
        OperatorConsoleStatutoryEvidenceReviewResult result) =>
        new(
            result.StatutoryDiscountDecisionCommandId,
            result.EvidenceSetReference,
            result.SourceChannel,
            result.DecisionResultStatus,
            result.ReviewStatus,
            result.EvidenceRequired,
            result.EvidenceRecorded,
            result.SetStatus,
            result.RetentionStatus,
            result.DeletionStatus,
            result.HoldActive,
            result.ReplacementPosture,
            result.Items.Select(item => new OperatorConsoleStatutoryEvidenceReviewItem(
                item.EvidenceItemReference,
                item.DocumentType,
                item.ItemRole,
                item.DeclaredContentType,
                item.AuthoritativeContentType,
                item.ContentLength,
                item.UploadStatus,
                item.ValidationStatus,
                item.ScanStatus,
                item.ReviewabilityStatus,
                item.BindingStatus,
                item.RetentionStatus,
                item.DeletionStatus,
                item.HoldActive,
                item.UploadedAt,
                item.FinalizedAt,
                item.ValidatedAt,
                item.ScannedAt,
                item.ReviewableAt,
                item.PreviewPermitted,
                item.PreviewDenialReason)).ToArray(),
            result.CorrelationId);

    private static ErrorResponse Error(string code, string message, Guid correlationId, bool retryable = false) =>
        new()
        {
            ErrorCode = code,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };

    private sealed class PreviewStreamResult(
        StatutoryEvidenceObjectContent content,
        OperatorConsoleStatutoryEvidencePreviewAuditContext auditContext,
        IOperatorConsoleStatutoryEvidenceReviewService service,
        ILogger logger) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = content.ContentType;
            response.ContentLength = content.ContentLength;
            response.Headers.CacheControl = "no-store, private, max-age=0";
            response.Headers.Pragma = "no-cache";
            response.Headers.ContentDisposition = "inline";
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers.XFrameOptions = "SAMEORIGIN";
            response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'self'; sandbox";

            try
            {
                await content.Content.CopyToAsync(response.Body, 81920, httpContext.RequestAborted).ConfigureAwait(false);
                await service.RecordPreviewStreamOutcomeAsync(auditContext, "COMPLETED", CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                await RecordOutcomeSafelyAsync("CANCELLED").ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Operator Console statutory evidence preview stream failed with controlled exception type {ExceptionType} and correlation {CorrelationId}.",
                    exception.GetType().Name,
                    auditContext.Target.CorrelationId);
                await RecordOutcomeSafelyAsync("FAILED").ConfigureAwait(false);
                httpContext.Abort();
            }
            finally
            {
                await content.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task RecordOutcomeSafelyAsync(string outcome)
        {
            try
            {
                await service.RecordPreviewStreamOutcomeAsync(auditContext, outcome, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Operator Console statutory evidence preview outcome audit failed with controlled exception type {ExceptionType} and correlation {CorrelationId}.",
                    exception.GetType().Name,
                    auditContext.Target.CorrelationId);
            }
        }
    }
}
