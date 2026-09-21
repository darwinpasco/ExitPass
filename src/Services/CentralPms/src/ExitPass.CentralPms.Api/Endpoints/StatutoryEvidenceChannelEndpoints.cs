using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.StatutoryEvidence;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class StatutoryEvidenceChannelEndpoints
{
    private const string WebPayPolicy = "WebPayStatutoryEvidenceCapture";
    private const string AptPolicy = "AptStatutoryEvidenceCapture";

    public static IEndpointRouteBuilder MapStatutoryEvidenceChannelEndpoints(this IEndpointRouteBuilder app)
    {
        MapChannel(
            app.MapGroup("/v1/webpay/statutory-discounts/evidence")
                .WithTags("WebPay")
                .AcceptAuthenticatedServicePrincipal(),
            StatutoryEvidenceChannelConstants.WebPay,
            WebPayPolicy,
            "WebPay");

        MapChannel(
            app.MapGroup("/v1/apt/statutory-discounts/evidence")
                .WithTags("AssistedPaymentTerminal")
                .AcceptAuthenticatedServicePrincipal(),
            StatutoryEvidenceChannelConstants.AssistedPaymentTerminal,
            AptPolicy,
            "Apt");

        return app;
    }

    private static void MapChannel(RouteGroupBuilder group, string sourceChannel, string policy, string namePrefix)
    {
        group.MapPost("/bootstrap", (StatutoryEvidenceChannelBootstrapRequest body, HttpRequest request, IStatutoryEvidenceChannelService service) =>
                BootstrapAsync(sourceChannel, body, request, service))
            .WithName($"{namePrefix}StatutoryEvidenceBootstrap")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Produces<StatutoryEvidenceChannelResponseDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary($"{namePrefix} statutory evidence bootstrap")
            .WithDescription("Derives evidence governance server-side and creates or rediscovers channel-safe evidence metadata without exposing object-storage internals.");

        group.MapGet("/status", (Guid? statutoryDiscountDecisionCommandId, Guid? evidenceSetReference, HttpRequest request, IStatutoryEvidenceChannelService service) =>
                StatusAsync(sourceChannel, statutoryDiscountDecisionCommandId, evidenceSetReference, request, service))
            .WithName($"{namePrefix}StatutoryEvidenceStatus")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Produces<StatutoryEvidenceChannelResponseDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary($"{namePrefix} statutory evidence status")
            .WithDescription("Returns authoritative channel-safe evidence lifecycle and readiness classifications.");

        group.MapPost("/upload-sessions", (StatutoryEvidenceChannelUploadSessionRequest body, HttpRequest request, IStatutoryEvidenceChannelService service) =>
                CreateUploadSessionAsync(sourceChannel, body, request, service))
            .WithName($"{namePrefix}StatutoryEvidenceUploadSession")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Produces<StatutoryEvidenceOpaqueUploadSessionResponseDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary($"{namePrefix} statutory evidence opaque upload session")
            .WithDescription("Issues an opaque upload-session reference only. The response does not include provider URL, bucket, object key, checksum, credentials, or storage headers.");

        group.MapPut("/upload-sessions/{opaqueUploadSessionReference:guid}", (Guid opaqueUploadSessionReference, HttpRequest request, IStatutoryEvidenceChannelService service) =>
                UploadAsync(sourceChannel, opaqueUploadSessionReference, request, service))
            .DisableAntiforgery()
            .WithName($"{namePrefix}StatutoryEvidenceUploadSessionBytes")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Accepts<Stream>("image/jpeg", "image/png")
            .Produces<StatutoryEvidenceOpaqueUploadSessionResponseDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary($"{namePrefix} statutory evidence upload relay")
            .WithDescription("Streams bounded evidence bytes through Central PMS to protected object storage. Bytes are not persisted in PostgreSQL, logs, or DTOs.");

        group.MapPost("/upload-sessions/{opaqueUploadSessionReference:guid}/finalize", (Guid opaqueUploadSessionReference, StatutoryEvidenceChannelFinalizeRequest body, HttpRequest request, IStatutoryEvidenceChannelService service) =>
                FinalizeUploadSessionAsync(sourceChannel, opaqueUploadSessionReference, body, request, service))
            .WithName($"{namePrefix}StatutoryEvidenceFinalizeUploadSession")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Produces<StatutoryEvidenceChannelResponseDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithSummary($"{namePrefix} statutory evidence upload-session finalization")
            .WithDescription("Finalizes the protected object through the existing I-013 server-side metadata verification path.");

        group.MapPost("/preview", (StatutoryEvidenceChannelPreviewRequest body, HttpRequest request, IStatutoryEvidenceChannelService service, ILoggerFactory loggerFactory) =>
                PreviewAsync(sourceChannel, body, request, service, loggerFactory))
            .WithName($"{namePrefix}StatutoryEvidencePreview")
            .WithMetadata(new ReconciliationPolicyMetadata(policy))
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithSummary($"{namePrefix} protected statutory evidence preview")
            .WithDescription("Streams a current, validated, malware-clean JPEG or PNG without exposing object-storage internals.");

        if (sourceChannel == StatutoryEvidenceChannelConstants.AssistedPaymentTerminal)
        {
            group.MapPost("/revalidate", (StatutoryEvidenceChannelBootstrapRequest body, HttpRequest request, IStatutoryEvidenceChannelService service) =>
                    StatusAsync(sourceChannel, body.StatutoryDiscountDecisionCommandId, null, request, service))
                .WithName($"{namePrefix}StatutoryEvidenceRevalidate")
                .WithMetadata(new ReconciliationPolicyMetadata(policy))
                .Produces<StatutoryEvidenceChannelResponseDto>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
                .WithSummary("APT statutory evidence readiness revalidation");
        }
    }

    private static async Task<IResult> BootstrapAsync(
        string sourceChannel,
        StatutoryEvidenceChannelBootstrapRequest body,
        HttpRequest request,
        IStatutoryEvidenceChannelService service)
    {
        var correlationId = ResolveCorrelation(request);
        if (!TryResolveActor(request, sourceChannel, correlationId, out var actor, out var denied))
        {
            return denied!;
        }

        var result = await service.BootstrapAsync(
            new StatutoryEvidenceChannelBootstrapCommand(sourceChannel, body.StatutoryDiscountDecisionCommandId, body.ClientOperationKey, correlationId, actor),
            request.HttpContext.RequestAborted);
        return ToChannelResult(result);
    }

    private static async Task<IResult> StatusAsync(
        string sourceChannel,
        Guid? statutoryDiscountDecisionCommandId,
        Guid? evidenceSetReference,
        HttpRequest request,
        IStatutoryEvidenceChannelService service)
    {
        var correlationId = ResolveCorrelation(request);
        if (!TryResolveActor(request, sourceChannel, correlationId, out var actor, out var denied))
        {
            return denied!;
        }

        var result = await service.GetStatusAsync(
            new StatutoryEvidenceChannelStatusQuery(sourceChannel, statutoryDiscountDecisionCommandId, evidenceSetReference, correlationId, actor),
            request.HttpContext.RequestAborted);
        return ToChannelResult(result);
    }

    private static async Task<IResult> CreateUploadSessionAsync(
        string sourceChannel,
        StatutoryEvidenceChannelUploadSessionRequest body,
        HttpRequest request,
        IStatutoryEvidenceChannelService service)
    {
        var correlationId = ResolveCorrelation(request);
        if (!TryResolveActor(request, sourceChannel, correlationId, out var actor, out var denied))
        {
            return denied!;
        }

        var result = await service.CreateUploadSessionAsync(
            new StatutoryEvidenceChannelUploadSessionCommand(
                sourceChannel,
                body.EvidenceSetReference,
                body.EvidenceItemReference,
                NormalizeContentType(body.DeclaredContentType),
                body.DeclaredContentLength,
                body.DeclaredChecksumSha256,
                body.ClientOperationKey,
                correlationId,
                actor),
            request.HttpContext.RequestAborted);
        return ToUploadSessionResult(result);
    }

    private static async Task<IResult> UploadAsync(
        string sourceChannel,
        Guid opaqueUploadSessionReference,
        HttpRequest request,
        IStatutoryEvidenceChannelService service)
    {
        var correlationId = ResolveCorrelation(request);
        if (!TryResolveActor(request, sourceChannel, correlationId, out var actor, out var denied))
        {
            return denied!;
        }

        var result = await service.UploadAsync(
            new StatutoryEvidenceChannelUploadCommand(
                sourceChannel,
                opaqueUploadSessionReference,
                NormalizeContentType(request.ContentType),
                request.ContentLength,
                request.Body,
                correlationId,
                actor),
            request.HttpContext.RequestAborted);
        return ToUploadSessionResult(result);
    }

    private static async Task<IResult> FinalizeUploadSessionAsync(
        string sourceChannel,
        Guid opaqueUploadSessionReference,
        StatutoryEvidenceChannelFinalizeRequest body,
        HttpRequest request,
        IStatutoryEvidenceChannelService service)
    {
        var correlationId = ResolveCorrelation(request);
        if (!TryResolveActor(request, sourceChannel, correlationId, out var actor, out var denied))
        {
            return denied!;
        }

        var result = await service.FinalizeUploadSessionAsync(
            new StatutoryEvidenceChannelFinalizeCommand(sourceChannel, opaqueUploadSessionReference, body.ClientOperationKey, correlationId, actor),
            request.HttpContext.RequestAborted);
        return ToChannelResult(result);
    }

    private static async Task<IResult> PreviewAsync(
        string sourceChannel,
        StatutoryEvidenceChannelPreviewRequest body,
        HttpRequest request,
        IStatutoryEvidenceChannelService service,
        ILoggerFactory loggerFactory)
    {
        var correlationId = ResolveCorrelation(request);
        if (!TryResolveActor(request, sourceChannel, correlationId, out var actor, out var denied))
        {
            return denied!;
        }

        var result = await service.OpenPreviewAsync(
            new StatutoryEvidenceChannelPreviewCommand(
                sourceChannel,
                body.StatutoryDiscountDecisionCommandId,
                body.EvidenceItemReference,
                correlationId,
                actor),
            request.HttpContext.RequestAborted).ConfigureAwait(false);

        if (result.Content is not null && result.AuditContext is not null)
        {
            return new PreviewStreamResult(
                result.Content,
                result.AuditContext,
                service,
                loggerFactory.CreateLogger("ExitPass.CentralPms.Api.StatutoryEvidenceChannelPreview"));
        }

        var status = result.ErrorCode switch
        {
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA" => StatusCodes.Status415UnsupportedMediaType,
            "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status409Conflict
        };
        return Results.Json(
            BuildError(
                result.ErrorCode ?? "STATUTORY_EVIDENCE_PREVIEW_NOT_ELIGIBLE",
                "The statutory evidence preview is unavailable.",
                result.CorrelationId,
                result.Retryable),
            statusCode: status);
    }

    private static IResult ToChannelResult(StatutoryEvidenceChannelResponse result) =>
        result.Classification == "REJECTED"
            ? Results.BadRequest(ToDto(result))
            : Results.Ok(ToDto(result));

    private static IResult ToUploadSessionResult(StatutoryEvidenceOpaqueUploadSessionResponse result) =>
        result.Classification is "REJECTED" or "SEMANTIC_CONFLICT"
            ? Results.BadRequest(ToDto(result))
            : Results.Ok(ToDto(result));

    private static StatutoryEvidenceChannelResponseDto ToDto(StatutoryEvidenceChannelResponse result) =>
        new(result.Classification, result.Retryable, result.ErrorCode, result.CorrelationId, result.SourceChannel, result.EvidenceRequired, result.EvidenceSetReference, result.EvidenceItemReference, result.AllowedContentTypes, result.MaximumContentLengthBytes, result.MaximumImageWidth, result.MaximumImageHeight, result.MaximumImagePixelCount, result.RequiredDocumentType, result.RequiredItemRole, result.LifecycleClassification, result.ReplacementPosture, result.ReadyForReview, result.ReadyForAptPreCash, result.BlockingReasonCode, result.EvaluatedAt);

    private static StatutoryEvidenceOpaqueUploadSessionResponseDto ToDto(StatutoryEvidenceOpaqueUploadSessionResponse result) =>
        new(result.Classification, result.Retryable, result.ErrorCode, result.CorrelationId, result.OpaqueUploadSessionReference, result.Method, result.ExpiresAt, result.AcceptedContentType, result.MaximumContentLengthBytes);

    private static bool TryResolveActor(
        HttpRequest request,
        string sourceChannel,
        Guid correlationId,
        out StatutoryEvidenceActor actor,
        out IResult? denied)
    {
        denied = null;
        actor = new StatutoryEvidenceActor(null, null, sourceChannel);
        var principal = request.HttpContext.User;
        if (principal.FindFirst("user_id") is not null ||
            principal.FindFirst("sub") is not null ||
            principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier) is not null)
        {
            denied = Results.Json(BuildError("FORBIDDEN_HUMAN_ACTOR", "A service identity is required for statutory evidence channel operations.", correlationId, false), statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        if (principal.Identity?.IsAuthenticated != true ||
            !string.Equals(principal.Identity.AuthenticationType, "InternalMtlsServicePrincipal", StringComparison.Ordinal) ||
            !string.Equals(principal.FindFirst("exitpass_audience")?.Value, "CENTRAL_PMS", StringComparison.Ordinal))
        {
            denied = Results.Json(BuildError("CENTRAL_PMS_SERVICE_PRINCIPAL_ADMISSION_DENIED", "An authenticated service principal for this statutory evidence channel is required.", correlationId, false), statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        if (!string.Equals(principal.FindFirst("source_channel")?.Value, sourceChannel, StringComparison.Ordinal))
        {
            denied = Results.Json(BuildError("CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH", "The authenticated service principal does not match this statutory evidence channel.", correlationId, false), statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        var rawServiceIdentity = principal.FindFirst("service_identity_id")?.Value ??
                                 principal.FindFirst("client_id")?.Value;
        if (!Guid.TryParse(rawServiceIdentity, out var serviceIdentityId) || serviceIdentityId == Guid.Empty)
        {
            denied = Results.Json(BuildError("SERVICE_IDENTITY_REQUIRED", "A service identity is required for statutory evidence channel operations.", correlationId, false), statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        actor = new StatutoryEvidenceActor(null, serviceIdentityId, sourceChannel);
        return true;
    }

    private static Guid ResolveCorrelation(HttpRequest request) =>
        Guid.TryParse(request.Headers["X-Correlation-Id"].FirstOrDefault(), out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();

    private static string NormalizeContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Split(';', 2)[0].Trim().ToLowerInvariant();

    private static ErrorResponse BuildError(string code, string message, Guid correlationId, bool retryable) =>
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
        IStatutoryEvidenceChannelService service,
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
                logger.LogError(exception, "Statutory evidence preview stream failed. CorrelationId: {CorrelationId}", auditContext.Target.CorrelationId);
            }
            finally
            {
                await content.DisposeAsync().ConfigureAwait(false);
                try
                {
                    await service.RecordPreviewStreamOutcomeAsync(auditContext, outcome, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Statutory evidence preview outcome audit failed. CorrelationId: {CorrelationId}", auditContext.Target.CorrelationId);
                }
            }
        }
    }
}

public sealed record StatutoryEvidenceChannelPreviewRequest(
    Guid StatutoryDiscountDecisionCommandId,
    Guid EvidenceItemReference);
