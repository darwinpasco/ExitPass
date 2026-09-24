using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class ManagementStatutoryBenefitReviewEndpoints
{
    public const string RoutePrefix = "/v1/management-platform/statutory-benefit-requests";

    public static IEndpointRouteBuilder MapManagementStatutoryBenefitReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags("ManagementPlatform", "StatutoryBenefitReview")
            .RequireAuthorization()
            .AddEndpointFilter(ValidateWebMutationAsync);

        group.MapGet("", ListAsync)
            .WithName("ListManagementStatutoryBenefitRequests")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementStatutoryBenefitReviewValues.ListPolicy));
        group.MapGet("/{decisionCommandReference:guid}", GetAsync)
            .WithName("GetManagementStatutoryBenefitRequest")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementStatutoryBenefitReviewValues.DetailPolicy));
        group.MapGet("/{decisionCommandReference:guid}/evidence", GetEvidenceAsync)
            .WithName("GetManagementStatutoryBenefitRequestEvidence")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementStatutoryBenefitReviewValues.EvidencePolicy));
        group.MapPost("/{decisionCommandReference:guid}/evidence/preview", PreviewEvidenceAsync)
            .WithName("PreviewManagementStatutoryBenefitRequestEvidence")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementStatutoryBenefitReviewValues.EvidencePolicy));
        group.MapPost("/{decisionCommandReference:guid}/decision", DecideAsync)
            .WithName("DecideManagementStatutoryBenefitRequest")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementStatutoryBenefitReviewValues.DecisionPolicy));

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        IIdentityAdministrationActorAccessor actors,
        IManagementStatutoryBenefitReviewService service,
        string? status,
        Guid? siteReference,
        string? sourceChannel,
        string? benefitType,
        DateTimeOffset? submittedFrom,
        DateTimeOffset? submittedTo,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(request);
        var actor = actors.Current;
        if (actor is null) return Error(401, "HUMAN_SESSION_REQUIRED", "An authenticated Management Platform session is required.", correlationId);
        try
        {
            var result = await service.ListAsync(actor, new(
                status ?? "PENDING", siteReference, sourceChannel, benefitType, submittedFrom, submittedTo,
                search, page ?? 1, pageSize ?? 25, correlationId), cancellationToken);
            return ToResult(result);
        }
        catch (Exception exception)
        {
            LogUnexpected(request, exception, correlationId);
            return Error(500, "STATUTORY_BENEFIT_REVIEW_UNEXPECTED_FAILURE", "The statutory-benefit review list failed safely.", correlationId);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid decisionCommandReference,
        HttpRequest request,
        IIdentityAdministrationActorAccessor actors,
        IManagementStatutoryBenefitReviewService service,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(request);
        var actor = actors.Current;
        if (actor is null) return Error(401, "HUMAN_SESSION_REQUIRED", "An authenticated Management Platform session is required.", correlationId);
        try
        {
            return ToResult(await service.GetAsync(actor, decisionCommandReference, correlationId, cancellationToken));
        }
        catch (Exception exception)
        {
            LogUnexpected(request, exception, correlationId);
            return Error(500, "STATUTORY_BENEFIT_REVIEW_UNEXPECTED_FAILURE", "The statutory-benefit request could not be read safely.", correlationId);
        }
    }

    private static async Task<IResult> DecideAsync(
        Guid decisionCommandReference,
        ManagementStatutoryBenefitDecisionRequest body,
        HttpRequest request,
        IIdentityAdministrationActorAccessor actors,
        IManagementStatutoryBenefitReviewService service,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(request);
        var actor = actors.Current;
        if (actor is null) return Error(401, "HUMAN_SESSION_REQUIRED", "An authenticated Management Platform session is required.", correlationId);
        try
        {
            return ToResult(await service.DecideAsync(actor, new(
                decisionCommandReference,
                body.Decision,
                body.RejectionReason,
                body.ExpectedVersion,
                body.IdempotencyKey,
                body.IdDocumentType,
                body.IssuingAuthority,
                body.ExpiryDate,
                body.IdControlReference,
                correlationId), cancellationToken));
        }
        catch (Exception exception)
        {
            LogUnexpected(request, exception, correlationId);
            return Error(500, "STATUTORY_BENEFIT_DECISION_UNEXPECTED_FAILURE", "The statutory-benefit decision failed safely.", correlationId);
        }
    }

    private static async Task<IResult> GetEvidenceAsync(
        Guid decisionCommandReference,
        HttpRequest request,
        IIdentityAdministrationActorAccessor actors,
        IManagementStatutoryBenefitReviewService service,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(request);
        var actor = actors.Current;
        if (actor is null) return Error(401, "HUMAN_SESSION_REQUIRED", "An authenticated Management Platform session is required.", correlationId);
        try
        {
            return ToResult(await service.GetEvidenceAsync(actor, decisionCommandReference, correlationId, cancellationToken));
        }
        catch (Exception exception)
        {
            LogUnexpected(request, exception, correlationId);
            return Error(503, "STATUTORY_BENEFIT_EVIDENCE_UNAVAILABLE", "The evidence metadata is unavailable.", correlationId, true);
        }
    }

    private static async Task<IResult> PreviewEvidenceAsync(
        Guid decisionCommandReference,
        ManagementStatutoryBenefitEvidencePreviewRequest body,
        HttpRequest request,
        IIdentityAdministrationActorAccessor actors,
        IManagementStatutoryBenefitReviewService service,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(request);
        var actor = actors.Current;
        if (actor is null) return Error(401, "HUMAN_SESSION_REQUIRED", "An authenticated Management Platform session is required.", correlationId);
        try
        {
            var result = await service.OpenEvidencePreviewAsync(
                actor,
                decisionCommandReference,
                body.EvidenceItemReference,
                correlationId,
                cancellationToken);
            if (result.Content is not null && result.AuditContext is not null)
            {
                return new PreviewStreamResult(
                    result.Content,
                    result.AuditContext,
                    service,
                    request.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ExitPass.CentralPms.Api.ManagementStatutoryBenefitEvidencePreview"));
            }

            return Error(
                result.ErrorCode == "NOT_FOUND" ? 404 : result.Retryable ? 503 : 409,
                result.ErrorCode ?? "STATUTORY_BENEFIT_EVIDENCE_PREVIEW_UNAVAILABLE",
                "The statutory-benefit evidence preview is unavailable.",
                result.CorrelationId,
                result.Retryable);
        }
        catch (Exception exception)
        {
            LogUnexpected(request, exception, correlationId);
            return Error(503, "STATUTORY_BENEFIT_EVIDENCE_PREVIEW_UNAVAILABLE", "The statutory-benefit evidence preview is unavailable.", correlationId, true);
        }
    }

    private static IResult ToResult<T>(ManagementStatutoryBenefitReviewResult<T> result) => result.Outcome switch
    {
        ManagementStatutoryBenefitReviewOutcome.Success => Results.Ok(result.Value),
        ManagementStatutoryBenefitReviewOutcome.Invalid => Error(400, result.Classification, result.Message, result.CorrelationId),
        ManagementStatutoryBenefitReviewOutcome.Forbidden => Error(403, result.Classification, result.Message, result.CorrelationId),
        ManagementStatutoryBenefitReviewOutcome.NotFound => Error(404, result.Classification, result.Message, result.CorrelationId),
        ManagementStatutoryBenefitReviewOutcome.Conflict => Error(409, result.Classification, result.Message, result.CorrelationId),
        ManagementStatutoryBenefitReviewOutcome.SourceUnavailable => Error(503, result.Classification, result.Message, result.CorrelationId, true),
        _ => Error(500, "STATUTORY_BENEFIT_REVIEW_UNEXPECTED_FAILURE", "The request failed safely.", result.CorrelationId)
    };

    private static async ValueTask<object?> ValidateWebMutationAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        var correlationId = ResolveCorrelationId(request);
        var originValidator = context.HttpContext.RequestServices.GetRequiredService<IHumanAuthenticationOriginValidator>();
        if (!originValidator.IsAllowed(request))
        {
            return Error(403, "STATUTORY_BENEFIT_REVIEW_ORIGIN_NOT_ALLOWED", "The request origin is not allowed.", correlationId);
        }

        try
        {
            await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Error(400, "STATUTORY_BENEFIT_REVIEW_CSRF_VALIDATION_FAILED", "The decision request could not be validated.", correlationId);
        }

        return await next(context);
    }

    private static Guid ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var value) && Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed : Guid.NewGuid();

    private static IResult Error(int status, string classification, string message, Guid correlationId, bool retryable = false) =>
        Results.Json(new { classification, message, correlationId, retryable }, statusCode: status);

    private static void LogUnexpected(HttpRequest request, Exception exception, Guid correlationId) =>
        request.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ExitPass.CentralPms.Api.ManagementStatutoryBenefitReviewEndpoints")
            .LogError(exception, "Management statutory-benefit review failed. CorrelationId: {CorrelationId}", correlationId);

    private sealed class PreviewStreamResult(
        StatutoryEvidenceObjectContent content,
        OperatorConsoleStatutoryEvidencePreviewAuditContext auditContext,
        IManagementStatutoryBenefitReviewService service,
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

            var outcome = "COMPLETED";
            try
            {
                await content.Content.CopyToAsync(response.Body, 81920, httpContext.RequestAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                outcome = "CANCELLED";
            }
            catch (Exception exception)
            {
                outcome = "FAILED";
                logger.LogError(exception, "Management statutory-benefit evidence preview stream failed. CorrelationId: {CorrelationId}", auditContext.Target.CorrelationId);
            }
            finally
            {
                await content.DisposeAsync().ConfigureAwait(false);
                try
                {
                    await service.RecordEvidencePreviewStreamOutcomeAsync(auditContext, outcome, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Management statutory-benefit evidence preview audit failed. CorrelationId: {CorrelationId}", auditContext.Target.CorrelationId);
                }
            }
        }
    }
}

public sealed record ManagementStatutoryBenefitDecisionRequest(
    string Decision,
    string? RejectionReason,
    long ExpectedVersion,
    string IdempotencyKey,
    string? IdDocumentType,
    string? IssuingAuthority,
    DateOnly? ExpiryDate,
    string? IdControlReference);

public sealed record ManagementStatutoryBenefitEvidencePreviewRequest(Guid EvidenceItemReference);
