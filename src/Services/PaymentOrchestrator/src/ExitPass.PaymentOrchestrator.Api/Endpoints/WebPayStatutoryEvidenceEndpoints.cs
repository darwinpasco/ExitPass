using System.Text.RegularExpressions;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;

namespace ExitPass.PaymentOrchestrator.Api.Endpoints;

/// <summary>Maps the browser-safe WebPay statutory-evidence boundary.</summary>
public static partial class WebPayStatutoryEvidenceEndpoints
{
    private static readonly ISet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };

    /// <summary>Maps channel-safe evidence bootstrap, status, upload, and finalization routes.</summary>
    public static IEndpointRouteBuilder MapWebPayStatutoryEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webpay/statutory-discounts/evidence/bootstrap", async (
            WebPayStatutoryEvidenceBootstrapRequest request,
            ICentralPmsWebPayStatutoryEvidenceClient client,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var correlationId = ReadOrCreateCorrelationId(context);
            if (request.StatutoryDiscountDecisionCommandId == Guid.Empty)
            {
                return Invalid(context, correlationId, "WEBPAY_STATUTORY_EVIDENCE_DECISION_REQUIRED", "The parking privilege request could not be identified.");
            }

            var result = await client.BootstrapAsync(
                new CentralPmsStatutoryEvidenceBootstrapRequest(request.StatutoryDiscountDecisionCommandId, NormalizeOperationKey(request.ClientOperationKey)),
                correlationId,
                cancellationToken);
            return ToChannelResult(result, context, correlationId);
        });

        app.MapGet("/v1/webpay/statutory-discounts/evidence/status", async (
            Guid? statutoryDiscountDecisionCommandId,
            Guid? evidenceSetReference,
            ICentralPmsWebPayStatutoryEvidenceClient client,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var correlationId = ReadOrCreateCorrelationId(context);
            var hasDecision = statutoryDiscountDecisionCommandId is Guid decisionId && decisionId != Guid.Empty;
            var hasSet = evidenceSetReference is Guid setReference && setReference != Guid.Empty;
            if (hasDecision == hasSet)
            {
                return Invalid(context, correlationId, "WEBPAY_STATUTORY_EVIDENCE_LOOKUP_INVALID", "Choose one parking privilege evidence lookup.");
            }

            var result = await client.GetStatusAsync(
                hasDecision ? statutoryDiscountDecisionCommandId : null,
                hasSet ? evidenceSetReference : null,
                correlationId,
                cancellationToken);
            return ToChannelResult(result, context, correlationId);
        });

        app.MapPost("/v1/webpay/statutory-discounts/evidence/upload-sessions", async (
            WebPayStatutoryEvidenceUploadSessionRequest request,
            ICentralPmsWebPayStatutoryEvidenceClient client,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var correlationId = ReadOrCreateCorrelationId(context);
            var contentType = NormalizeContentType(request.DeclaredContentType);
            if (request.EvidenceSetReference == Guid.Empty || request.EvidenceItemReference == Guid.Empty ||
                !AllowedContentTypes.Contains(contentType) || request.DeclaredContentLength <= 0 ||
                string.IsNullOrWhiteSpace(request.DeclaredChecksumSha256) || !Sha256Pattern().IsMatch(request.DeclaredChecksumSha256))
            {
                return Invalid(context, correlationId, "WEBPAY_STATUTORY_EVIDENCE_FILE_INVALID", "Choose a valid JPEG or PNG image within the allowed size.");
            }

            var result = await client.CreateUploadSessionAsync(
                new CentralPmsStatutoryEvidenceUploadSessionRequest(
                    request.EvidenceSetReference,
                    request.EvidenceItemReference,
                    contentType,
                    request.DeclaredContentLength,
                    request.DeclaredChecksumSha256.ToLowerInvariant(),
                    NormalizeOperationKey(request.ClientOperationKey)),
                correlationId,
                cancellationToken);
            return ToUploadSessionResult(result, context, correlationId);
        });

        app.MapPut("/v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference:guid}", async (
            Guid opaqueUploadSessionReference,
            ICentralPmsWebPayStatutoryEvidenceClient client,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var correlationId = ReadOrCreateCorrelationId(context);
            var contentType = NormalizeContentType(context.Request.ContentType);
            if (opaqueUploadSessionReference == Guid.Empty || !AllowedContentTypes.Contains(contentType) || context.Request.ContentLength is not > 0)
            {
                return Invalid(context, correlationId, "WEBPAY_STATUTORY_EVIDENCE_UPLOAD_INVALID", "The selected image could not be uploaded. Choose the file again.");
            }

            var result = await client.UploadAsync(
                opaqueUploadSessionReference,
                contentType,
                context.Request.ContentLength.Value,
                context.Request.Body,
                correlationId,
                cancellationToken);
            return ToUploadSessionResult(result, context, correlationId);
        })
        .DisableAntiforgery();

        app.MapPost("/v1/webpay/statutory-discounts/evidence/upload-sessions/{opaqueUploadSessionReference:guid}/finalize", async (
            Guid opaqueUploadSessionReference,
            WebPayStatutoryEvidenceFinalizeRequest request,
            ICentralPmsWebPayStatutoryEvidenceClient client,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var correlationId = ReadOrCreateCorrelationId(context);
            if (opaqueUploadSessionReference == Guid.Empty)
            {
                return Invalid(context, correlationId, "WEBPAY_STATUTORY_EVIDENCE_UPLOAD_SESSION_REQUIRED", "The upload session has expired. Choose the image again.");
            }

            var result = await client.FinalizeAsync(
                opaqueUploadSessionReference,
                NormalizeOperationKey(request.ClientOperationKey),
                correlationId,
                cancellationToken);
            return ToChannelResult(result, context, correlationId);
        });

        return app;
    }

    private static IResult ToChannelResult(CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel> result, HttpContext context, Guid fallbackCorrelationId)
    {
        if (result.Succeeded && result.Value is not null)
        {
            context.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString("D");
            return Results.Ok(ToResponse(result.Value));
        }

        return ToSafeFailure(result.Error, context, fallbackCorrelationId);
    }

    private static IResult ToUploadSessionResult(CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession> result, HttpContext context, Guid fallbackCorrelationId)
    {
        if (result.Succeeded && result.Value is not null)
        {
            context.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString("D");
            return Results.Ok(ToResponse(result.Value));
        }

        return ToSafeFailure(result.Error, context, fallbackCorrelationId);
    }

    private static IResult ToSafeFailure(CentralPmsWebPayError? error, HttpContext context, Guid fallbackCorrelationId)
    {
        var resolved = error ?? new CentralPmsWebPayError(502, "STATUTORY_EVIDENCE_REQUEST_FAILED", "Evidence service failed.", true, fallbackCorrelationId);
        var correlationId = resolved.CorrelationId ?? fallbackCorrelationId;
        var authFailure = resolved.StatusCode is 401 or 403 || ContainsAny(resolved.ErrorCode, "AUTH", "FORBIDDEN", "PERMISSION", "SERVICE_IDENTITY", "SCOPE_DENIED");
        var transient = resolved.Retryable || resolved.StatusCode is 408 or 502 or 503 or 504 || ContainsAny(resolved.ErrorCode, "UNAVAILABLE", "TIMEOUT", "PROVIDER");
        var conflict = ContainsAny(resolved.ErrorCode, "CONFLICT", "ACTIVE_UPLOAD_SESSION_EXISTS", "LIFECYCLE_CONFLICT", "REVIEW_LOCKED");

        var statusCode = authFailure || transient
            ? StatusCodes.Status503ServiceUnavailable
            : conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var code = authFailure
            ? "WEBPAY_STATUTORY_EVIDENCE_SERVICE_UNAVAILABLE"
            : transient ? "WEBPAY_STATUTORY_EVIDENCE_TEMPORARILY_UNAVAILABLE"
            : conflict ? "WEBPAY_STATUTORY_EVIDENCE_CONFLICT"
            : MapSafeCode(resolved.ErrorCode);
        var message = authFailure
            ? "Evidence upload is temporarily unavailable. Please try again later or ask a parking attendant for assistance."
            : transient
                ? "We could not process the evidence upload right now. Please try again."
                : SafeMessage(resolved.ErrorCode);

        context.Response.Headers["X-Correlation-Id"] = correlationId.ToString("D");
        return Results.Json(new { errorCode = code, message, retryable = authFailure || transient, correlationId }, statusCode: statusCode);
    }

    private static IResult Invalid(HttpContext context, Guid correlationId, string code, string message)
    {
        context.Response.Headers["X-Correlation-Id"] = correlationId.ToString("D");
        return Results.Json(new { errorCode = code, message, retryable = false, correlationId }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static WebPayStatutoryEvidenceChannelResponse ToResponse(CentralPmsStatutoryEvidenceChannel value) => new()
    {
        Classification = value.Classification,
        Retryable = value.Retryable,
        ErrorCode = value.ErrorCode,
        CorrelationId = value.CorrelationId,
        EvidenceRequired = value.EvidenceRequired,
        EvidenceSetReference = value.EvidenceSetReference,
        EvidenceItemReference = value.EvidenceItemReference,
        AllowedContentTypes = value.AllowedContentTypes,
        MaximumContentLengthBytes = value.MaximumContentLengthBytes,
        MaximumImageWidth = value.MaximumImageWidth,
        MaximumImageHeight = value.MaximumImageHeight,
        MaximumImagePixelCount = value.MaximumImagePixelCount,
        RequiredDocumentType = value.RequiredDocumentType,
        RequiredItemRole = value.RequiredItemRole,
        LifecycleClassification = value.LifecycleClassification,
        ReplacementPosture = value.ReplacementPosture,
        ReadyForReview = value.ReadyForReview,
        BlockingReasonCode = value.BlockingReasonCode,
        EvaluatedAt = value.EvaluatedAt
    };

    private static WebPayStatutoryEvidenceUploadSessionResponse ToResponse(CentralPmsStatutoryEvidenceUploadSession value) => new()
    {
        Classification = value.Classification,
        Retryable = value.Retryable,
        ErrorCode = value.ErrorCode,
        CorrelationId = value.CorrelationId,
        OpaqueUploadSessionReference = value.OpaqueUploadSessionReference,
        Method = value.Method,
        ExpiresAt = value.ExpiresAt,
        AcceptedContentType = value.AcceptedContentType,
        MaximumContentLengthBytes = value.MaximumContentLengthBytes
    };

    private static Guid ReadOrCreateCorrelationId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["X-Correlation-Id"].FirstOrDefault(), out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();

    private static string NormalizeContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Split(';', 2)[0].Trim().ToLowerInvariant();

    private static string? NormalizeOperationKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 200)];

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string MapSafeCode(string code) => code switch
    {
        "AUTHORIZATION_EXPIRED" => "WEBPAY_STATUTORY_EVIDENCE_UPLOAD_EXPIRED",
        "AUTHORIZATION_NOT_USABLE" => "WEBPAY_STATUTORY_EVIDENCE_UPLOAD_EXPIRED",
        "UNSUPPORTED_CONTENT_TYPE" or "CONTENT_TYPE_MISMATCH" => "WEBPAY_STATUTORY_EVIDENCE_FILE_TYPE_INVALID",
        "CONTENT_LENGTH_EXCEEDED" or "CONTENT_LENGTH_MISMATCH" => "WEBPAY_STATUTORY_EVIDENCE_FILE_SIZE_INVALID",
        "CHECKSUM_MISMATCH" => "WEBPAY_STATUTORY_EVIDENCE_FILE_VERIFICATION_FAILED",
        "UNKNOWN_CONTEXT" => "WEBPAY_STATUTORY_EVIDENCE_CONTEXT_NOT_FOUND",
        _ => "WEBPAY_STATUTORY_EVIDENCE_REQUEST_FAILED"
    };

    private static string SafeMessage(string code) => code switch
    {
        "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_NOT_USABLE" => "The upload session has expired. Choose the image again.",
        "UNSUPPORTED_CONTENT_TYPE" or "CONTENT_TYPE_MISMATCH" => "Choose a JPEG or PNG image.",
        "CONTENT_LENGTH_EXCEEDED" or "CONTENT_LENGTH_MISMATCH" => "The selected image is too large.",
        "CHECKSUM_MISMATCH" => "The uploaded image could not be verified. Choose the image again.",
        "REVIEW_LOCKED" => "This evidence is already under review and cannot be replaced.",
        _ => "The evidence request could not be completed. Please try again."
    };

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
