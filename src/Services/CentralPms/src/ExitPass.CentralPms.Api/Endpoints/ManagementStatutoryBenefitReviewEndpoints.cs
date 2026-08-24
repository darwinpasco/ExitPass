using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
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
}

public sealed record ManagementStatutoryBenefitDecisionRequest(
    string Decision,
    string? RejectionReason,
    long ExpectedVersion,
    string IdempotencyKey);
