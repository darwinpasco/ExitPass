using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.StatutoryEvidence;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class StatutoryEvidenceMetadataEndpoints
{
    private const string EvidenceCapturePolicy = "CentralPmsStatutoryEvidenceCaptureMetadata";
    private const string EvidenceViewPolicy = "CentralPmsStatutoryEvidenceViewMetadata";
    private const string EvidenceHoldPolicy = "CentralPmsStatutoryEvidenceHoldManage";
    private const string EvidenceDeletePolicy = "CentralPmsStatutoryEvidenceDeletionRequest";

    public static IEndpointRouteBuilder MapStatutoryEvidenceMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/internal/statutory-discounts/evidence")
            .WithTags("StatutoryEvidence");

        group.MapPost("/sets", CreateSetAsync)
            .WithName("CreateStatutoryEvidenceSetMetadata")
            .Accepts<StatutoryEvidenceCreateSetRequest>("application/json")
            .Produces<StatutoryEvidenceOperationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceCapturePolicy))
            .WithSummary("Create or resolve statutory evidence set metadata")
            .WithDescription("Creates or idempotently resolves a metadata-only statutory evidence set. This endpoint does not upload, store, preview, scan, or return evidence bytes.");

        group.MapPost("/sets/{evidenceSetReference:guid}/items", AddItemAsync)
            .WithName("AddStatutoryEvidenceItemMetadata")
            .Accepts<StatutoryEvidenceAddItemRequest>("application/json")
            .Produces<StatutoryEvidenceOperationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceCapturePolicy))
            .WithSummary("Add statutory evidence item metadata")
            .WithDescription("Adds metadata for one controlled statutory evidence item. No evidence bytes, Base64, object key, signed URL, or checksum is accepted.");

        group.MapPost("/sets/{evidenceSetReference:guid}/items/{evidenceItemReference:guid}/upload-authorizations", AuthorizeUploadAsync)
            .WithName("AuthorizeStatutoryEvidenceItemUpload")
            .Accepts<StatutoryEvidenceUploadAuthorizationRequest>("application/json")
            .Produces<StatutoryEvidenceUploadAuthorizationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceCapturePolicy))
            .WithSummary("Authorize direct protected upload for a statutory evidence item")
            .WithDescription("Issues short-lived direct-upload authorization for an existing evidence item. Evidence bytes never pass through Central PMS, PostgreSQL, payment payloads, or POS fiscal payloads.");

        group.MapPost("/sets/{evidenceSetReference:guid}/items/{evidenceItemReference:guid}/upload-finalizations", FinalizeUploadAsync)
            .WithName("FinalizeStatutoryEvidenceItemUpload")
            .Accepts<StatutoryEvidenceUploadFinalizationRequest>("application/json")
            .Produces<StatutoryEvidenceUploadFinalizationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceCapturePolicy))
            .WithSummary("Finalize direct protected upload for a statutory evidence item")
            .WithDescription("Verifies provider object metadata server-side and marks upload complete without marking validation, scan, or reviewability as passed.");

        group.MapGet("/sets/{evidenceSetReference:guid}", GetSetAsync)
            .WithName("GetStatutoryEvidenceSetMetadata")
            .Produces<StatutoryEvidenceSetResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceViewPolicy))
            .WithSummary("Read statutory evidence set metadata")
            .WithDescription("Reads safe metadata-only evidence lifecycle state. Possession of the opaque reference is not sufficient; RBAC and scope checks remain server-side.");

        group.MapPost("/sets/{evidenceSetReference:guid}/lock-for-review", LockForReviewAsync)
            .WithName("LockStatutoryEvidenceSetForReview")
            .Accepts<StatutoryEvidenceTransitionRequest>("application/json")
            .Produces<StatutoryEvidenceOperationResponse>(StatusCodes.Status200OK)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceViewPolicy))
            .WithSummary("Lock statutory evidence metadata for review")
            .WithDescription("Locks metadata for review. This does not grant preview/download access and does not apply a statutory benefit.");

        group.MapPost("/sets/{evidenceSetReference:guid}/hold", PlaceHoldAsync)
            .WithName("PlaceStatutoryEvidenceHold")
            .Accepts<StatutoryEvidenceHoldRequest>("application/json")
            .Produces<StatutoryEvidenceOperationResponse>(StatusCodes.Status200OK)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceHoldPolicy))
            .WithSummary("Place statutory evidence hold")
            .WithDescription("Places metadata hold state that blocks deletion only. It does not broaden evidence view authority.");

        group.MapPost("/sets/{evidenceSetReference:guid}/hold/release", ReleaseHoldAsync)
            .WithName("ReleaseStatutoryEvidenceHold")
            .Accepts<StatutoryEvidenceTransitionRequest>("application/json")
            .Produces<StatutoryEvidenceOperationResponse>(StatusCodes.Status200OK)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceHoldPolicy))
            .WithSummary("Release statutory evidence hold");

        group.MapPost("/sets/{evidenceSetReference:guid}/deletion-request", RequestDeletionAsync)
            .WithName("RequestStatutoryEvidenceDeletion")
            .Accepts<StatutoryEvidenceTransitionRequest>("application/json")
            .Produces<StatutoryEvidenceOperationResponse>(StatusCodes.Status200OK)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceDeletePolicy))
            .WithSummary("Request statutory evidence metadata deletion")
            .WithDescription("Records deletion-request metadata only. Object deletion and secure erasure are outside this slice.");

        return app;
    }

    private static async Task<IResult> CreateSetAsync(
        StatutoryEvidenceCreateSetRequest request,
        Guid? correlationId,
        HttpRequest httpRequest,
        IStatutoryEvidenceMetadataService service)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await service.CreateOrResolveSetAsync(
            new StatutoryEvidenceCreateSetCommand(
                request.StatutoryDiscountDecisionCommandId,
                request.StatutoryDiscountValidationId,
                request.ParkingSessionId,
                request.SiteId,
                request.SiteGroupId,
                request.EntitlementType,
                request.RequiredDocumentProfileCode,
                request.RequiredDocumentProfileVersion,
                request.RetentionClassCode,
                request.RetentionPolicyVersion,
                request.EnvironmentScope,
                request.IdempotencyScope,
                request.IdempotencyKey,
                effectiveCorrelationId,
                ResolveActor(httpRequest, request.SourceChannel)),
            httpRequest.HttpContext.RequestAborted);
        return ToResult(result, effectiveCorrelationId);
    }

    private static async Task<IResult> AddItemAsync(Guid evidenceSetReference, StatutoryEvidenceAddItemRequest request, Guid? correlationId, HttpRequest httpRequest, IStatutoryEvidenceMetadataService service)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await service.AddItemAsync(new StatutoryEvidenceAddItemCommand(evidenceSetReference, request.DocumentType, request.ItemRole, request.ExpectedMediaClass, request.DeclaredContentType, request.ProfileCode, request.IdempotencyScope, request.IdempotencyKey, effectiveCorrelationId, ResolveActor(httpRequest, request.SourceChannel)), httpRequest.HttpContext.RequestAborted);
        return ToResult(result, effectiveCorrelationId);
    }

    private static async Task<IResult> AuthorizeUploadAsync(
        Guid evidenceSetReference,
        Guid evidenceItemReference,
        StatutoryEvidenceUploadAuthorizationRequest request,
        Guid? correlationId,
        HttpRequest httpRequest,
        IStatutoryEvidenceUploadService service)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await service.AuthorizeUploadAsync(
            new StatutoryEvidenceUploadAuthorizationCommand(
                evidenceSetReference,
                evidenceItemReference,
                request.DeclaredContentType,
                request.DeclaredContentLength,
                request.MediaClass,
                request.ChecksumAlgorithm,
                request.DeclaredChecksumSha256,
                request.IdempotencyScope,
                request.IdempotencyKey,
                effectiveCorrelationId,
                ResolveActor(httpRequest, request.SourceChannel)),
            httpRequest.HttpContext.RequestAborted);
        return ToUploadAuthorizationResult(result, effectiveCorrelationId);
    }

    private static async Task<IResult> FinalizeUploadAsync(
        Guid evidenceSetReference,
        Guid evidenceItemReference,
        StatutoryEvidenceUploadFinalizationRequest request,
        Guid? correlationId,
        HttpRequest httpRequest,
        IStatutoryEvidenceUploadService service)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await service.FinalizeUploadAsync(
            new StatutoryEvidenceUploadFinalizationCommand(
                evidenceSetReference,
                evidenceItemReference,
                request.UploadAuthorizationReference,
                request.IdempotencyScope,
                request.IdempotencyKey,
                effectiveCorrelationId,
                ResolveActor(httpRequest, request.SourceChannel)),
            httpRequest.HttpContext.RequestAborted);
        return ToUploadFinalizationResult(result, effectiveCorrelationId);
    }

    private static async Task<IResult> GetSetAsync(Guid evidenceSetReference, Guid? correlationId, HttpRequest httpRequest, IStatutoryEvidenceMetadataService service)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await service.GetEvidenceSetAsync(evidenceSetReference, ResolveActor(httpRequest, ResolveReadSourceChannel(httpRequest)), effectiveCorrelationId, httpRequest.HttpContext.RequestAborted);
        return result is null
            ? Results.NotFound(new ErrorResponse { ErrorCode = "STATUTORY_EVIDENCE_NOT_FOUND", Message = "The statutory evidence metadata was not found.", CorrelationId = effectiveCorrelationId, Retryable = false })
            : Results.Ok(ToResponse(result));
    }

    private static Task<IResult> LockForReviewAsync(Guid evidenceSetReference, StatutoryEvidenceTransitionRequest request, Guid? correlationId, HttpRequest httpRequest, IStatutoryEvidenceMetadataService service) =>
        TransitionAsync(evidenceSetReference, request, correlationId, httpRequest, service,
            (svc, command, reference, effectiveCorrelationId, actor, cancellationToken) =>
                svc.LockForReviewAsync(new StatutoryEvidenceLockForReviewCommand(reference, command.IdempotencyScope, command.IdempotencyKey, effectiveCorrelationId, actor), cancellationToken));

    private static async Task<IResult> PlaceHoldAsync(Guid evidenceSetReference, StatutoryEvidenceHoldRequest request, Guid? correlationId, HttpRequest httpRequest, IStatutoryEvidenceMetadataService service)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await service.PlaceHoldAsync(new StatutoryEvidenceHoldCommand(evidenceSetReference, request.ReasonCode, request.IdempotencyScope, request.IdempotencyKey, effectiveCorrelationId, ResolveActor(httpRequest, request.SourceChannel)), httpRequest.HttpContext.RequestAborted);
        return ToResult(result, effectiveCorrelationId);
    }

    private static Task<IResult> ReleaseHoldAsync(Guid evidenceSetReference, StatutoryEvidenceTransitionRequest request, Guid? correlationId, HttpRequest httpRequest, IStatutoryEvidenceMetadataService service) =>
        TransitionAsync(evidenceSetReference, request, correlationId, httpRequest, service,
            (svc, command, reference, effectiveCorrelationId, actor, cancellationToken) =>
                svc.ReleaseHoldAsync(new StatutoryEvidenceReleaseHoldCommand(reference, command.IdempotencyScope, command.IdempotencyKey, effectiveCorrelationId, actor), cancellationToken));

    private static Task<IResult> RequestDeletionAsync(Guid evidenceSetReference, StatutoryEvidenceTransitionRequest request, Guid? correlationId, HttpRequest httpRequest, IStatutoryEvidenceMetadataService service) =>
        TransitionAsync(evidenceSetReference, request, correlationId, httpRequest, service,
            (svc, command, reference, effectiveCorrelationId, actor, cancellationToken) =>
                svc.RequestDeletionAsync(new StatutoryEvidenceDeletionRequestCommand(reference, command.IdempotencyScope, command.IdempotencyKey, effectiveCorrelationId, actor), cancellationToken));

    private static async Task<IResult> TransitionAsync(
        Guid evidenceSetReference,
        StatutoryEvidenceTransitionRequest request,
        Guid? correlationId,
        HttpRequest httpRequest,
        IStatutoryEvidenceMetadataService service,
        Func<IStatutoryEvidenceMetadataService, StatutoryEvidenceTransitionRequest, Guid, Guid, StatutoryEvidenceActor, CancellationToken, Task<StatutoryEvidenceOperationOutcome>> transition)
    {
        var effectiveCorrelationId = ResolveCorrelation(correlationId, httpRequest);
        var result = await transition(service, request, evidenceSetReference, effectiveCorrelationId, ResolveActor(httpRequest, request.SourceChannel), httpRequest.HttpContext.RequestAborted);
        return ToResult(result, effectiveCorrelationId);
    }

    private static IResult ToResult(StatutoryEvidenceOperationOutcome result, Guid correlationId) =>
        result.Classification is "REJECTED" or "SEMANTIC_CONFLICT"
            ? Results.BadRequest(new StatutoryEvidenceOperationResponse(result.Classification, result.Retryable, result.ErrorCode, correlationId, ToResponse(result.EvidenceSet), ToItemResponse(result.EvidenceItem)))
            : Results.Ok(new StatutoryEvidenceOperationResponse(result.Classification, result.Retryable, result.ErrorCode, correlationId, ToResponse(result.EvidenceSet), ToItemResponse(result.EvidenceItem)));

    private static IResult ToUploadAuthorizationResult(StatutoryEvidenceUploadAuthorizationOutcome result, Guid correlationId) =>
        result.Classification is "REJECTED" or "SEMANTIC_CONFLICT"
            ? Results.BadRequest(new StatutoryEvidenceUploadAuthorizationResponse(result.Classification, result.Retryable, result.ErrorCode, correlationId, ToUploadAuthorizationResponse(result.UploadAuthorization), ToItemResponse(result.EvidenceItem)))
            : Results.Ok(new StatutoryEvidenceUploadAuthorizationResponse(result.Classification, result.Retryable, result.ErrorCode, correlationId, ToUploadAuthorizationResponse(result.UploadAuthorization), ToItemResponse(result.EvidenceItem)));

    private static IResult ToUploadFinalizationResult(StatutoryEvidenceUploadFinalizationOutcome result, Guid correlationId) =>
        result.Classification is "REJECTED" or "SEMANTIC_CONFLICT"
            ? Results.BadRequest(new StatutoryEvidenceUploadFinalizationResponse(result.Classification, result.Retryable, result.ErrorCode, correlationId, ToItemResponse(result.EvidenceItem)))
            : Results.Ok(new StatutoryEvidenceUploadFinalizationResponse(result.Classification, result.Retryable, result.ErrorCode, correlationId, ToItemResponse(result.EvidenceItem)));

    private static Guid ResolveCorrelation(Guid? correlationId, HttpRequest request) =>
        correlationId ?? (Guid.TryParse(request.Headers["X-Correlation-Id"], out var parsed) ? parsed : Guid.NewGuid());

    private static StatutoryEvidenceActor ResolveActor(HttpRequest request, string sourceChannel) =>
        new(
            TryGuid(request.Headers[CentralPmsRbacPolicyCatalog.UserIdHeaderName]),
            TryGuid(request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName]),
            sourceChannel);

    private static string ResolveReadSourceChannel(HttpRequest request) =>
        request.Headers.TryGetValue("X-ExitPass-Source-Channel", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : "CENTRAL_PMS";

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static StatutoryEvidenceSetResponse? ToResponse(StatutoryEvidenceSetReadModel? model) =>
        model is null ? null : new StatutoryEvidenceSetResponse(model.EvidenceSetReference, model.StatutoryDiscountDecisionCommandId, model.StatutoryDiscountValidationId, model.ParkingSessionId, model.SiteId, model.SiteGroupId, model.EntitlementType, model.SourceChannel, model.SetStatus, model.RequiredDocumentProfileCode, model.RequiredDocumentProfileVersion, model.RetentionClassCode, model.RetentionPolicyVersion, model.RetentionStatus, model.DeletionStatus, model.HoldActive, model.HoldReasonCode, model.CorrelationId, model.CreatedAt, model.UpdatedAt, model.Items.Select(ToResponse).ToArray());

    private static StatutoryEvidenceItemResponse? ToItemResponse(StatutoryEvidenceItemReadModel? model) =>
        model is null ? null : ToResponse(model);

    private static StatutoryEvidenceUploadAuthorizationDetailsResponse? ToUploadAuthorizationResponse(StatutoryEvidenceUploadAuthorizationReadModel? model) =>
        model is null ? null : new StatutoryEvidenceUploadAuthorizationDetailsResponse(model.UploadAuthorizationReference, model.UploadUrl, model.UploadMethod, model.RequiredHeaders, model.ExpiresAt, model.MaxContentLengthBytes, model.AcceptedContentType);

    private static StatutoryEvidenceItemResponse ToResponse(StatutoryEvidenceItemReadModel model) =>
        new(model.EvidenceItemReference, model.DocumentType, model.ItemRole, model.UploadStatus, model.ValidationStatus, model.ScanStatus, model.ReviewabilityStatus, model.BindingStatus, model.RetentionStatus, model.DeletionStatus, model.HoldActive, model.ExpectedMediaClass, model.DeclaredContentType, model.ProfileCode, model.ValidationResultClassification, model.ScanResultClassification, model.CreatedAt, model.UpdatedAt);
}
