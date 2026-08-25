using System.Diagnostics;
using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console statutory discount validation draft endpoint.
///
/// ExitPass v1.3 Invariants Enforced:
/// - This endpoint persists Operator Console access evaluation evidence before draft creation.
/// - This endpoint may persist a privacy-minimized statutory discount validation draft and metadata-only evidence reference.
/// - This endpoint may persist a review decision status transition on an existing validation draft.
/// - This endpoint never mutates PaymentAttempt, PaymentConfirmation,
///   ExitAuthorization, provider outcome, gate consume, coupon application, settlement truth,
///   reconciliation records, or payment finality.
/// </summary>
public static class OperatorConsoleStatutoryDiscountDraftEndpoints
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string DraftViewPolicy = "OperatorConsoleStatutoryDiscountDraftView";
    private const string DraftCreatePolicy = "OperatorConsoleStatutoryDiscountDraftCreate";
    private const string ServiceChannelReviewQueueReadPolicy = "OperatorConsoleStatutoryDiscountReviewQueueRead";
    private const string ServiceChannelReviewDetailReadPolicy = "OperatorConsoleStatutoryDiscountReviewDetailRead";
    private const string DecisionMutatePolicy = "OperatorConsoleStatutoryDiscountDecisionMutate";
    private const string EvidenceCapturePolicy = "OperatorConsoleStatutoryDiscountEvidenceCapture";
    private const string EvidenceViewPolicy = "OperatorConsoleStatutoryDiscountEvidenceView";
    private const string AuditReadPolicy = "OperatorConsoleStatutoryDiscountAuditRead";
    private const string ApprovePermission = "statutory-discounts.decision.approve";
    private const string RejectPermission = "statutory-discounts.decision.reject";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraft");
    private static readonly ActivitySource ReadActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountRead");
    private static readonly ActivitySource DecisionActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDecision");
    private static readonly ActivitySource EvidenceActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountEvidence");
    private static readonly ActivitySource ServiceChannelReviewActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleServiceChannelStatutoryDiscountReview");

    /// <summary>
    /// Maps Operator Console statutory discount draft endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleStatutoryDiscountDraftEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapGet("/statutory-discounts/drafts", ListDraftsAsync)
            .WithName("ListOperatorConsoleStatutoryDiscountDrafts")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleStatutoryDiscountDraftQueueResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(DraftViewPolicy))
            .WithSummary("List Operator Console statutory discount validation drafts")
            .WithDescription("Returns a read-only queue of Operator Console statutory discount validation drafts from stored validation, policy, tariff, and payable-basis metadata. This endpoint does not resolve policies, apply discounts, upload evidence, or mutate payment, gate, coupon, provider, payable, settlement, or reconciliation state.");

        group.MapGet("/statutory-discounts/drafts/{draftId:guid}", GetDraftAsync)
            .WithName("GetOperatorConsoleStatutoryDiscountDraft")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleStatutoryDiscountDraftDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(DraftViewPolicy))
            .WithSummary("Get Operator Console statutory discount validation draft detail")
            .WithDescription("Returns read-only detail for one Operator Console statutory discount validation draft using the stored policy snapshot and payable-basis metadata. This endpoint does not resolve policies, apply discounts, upload evidence, or mutate payment, gate, coupon, provider, payable, settlement, or reconciliation state.");

        group.MapGet("/audit/statutory-discounts", ListAuditReportAsync)
            .WithName("ListOperatorConsoleStatutoryDiscountAuditReport")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleStatutoryDiscountAuditReportResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(AuditReadPolicy))
            .WithSummary("List Operator Console statutory discount audit/reporting rows")
            .WithDescription("Returns a read-only statutory discount/access audit report using safe masked fields only. This endpoint does not return raw evidence, raw ID numbers, payment authority, gate authority, coupon authority, or reconciliation mutation.");

        group.MapGet("/statutory-discounts/reviews", ListServiceChannelReviewsAsync)
            .WithName("ListOperatorConsoleServiceChannelStatutoryDiscountReviews")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(ServiceChannelReviewQueueReadPolicy))
            .WithSummary("List canonical Central PMS statutory-review requests")
            .WithDescription("Returns a scope-filtered, paged Central PMS statutory-review queue. Requests may originate from WebPay or Assisted Payment Terminal, but Operator Console communicates only with Central PMS. This endpoint returns no evidence content or mutation authority.");

        group.MapGet("/statutory-discounts/reviews/pending", ListServiceChannelReviewsAsync)
            .WithName("ListOperatorConsolePendingServiceChannelStatutoryDiscountReviews")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse>(StatusCodes.Status200OK)
            .WithMetadata(new ReconciliationPolicyMetadata(ServiceChannelReviewQueueReadPolicy))
            .WithSummary("Compatibility alias for the Central PMS statutory-review queue");

        group.MapGet("/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId:guid}", GetServiceChannelReviewAsync)
            .WithName("GetOperatorConsoleServiceChannelStatutoryDiscountReview")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(ServiceChannelReviewDetailReadPolicy))
            .WithSummary("Get service-channel statutory discount review detail")
            .WithDescription("Returns safe submitted facts and evidence references for one service-channel statutory-discount decision awaiting Operator Console review. It does not return raw evidence, raw statutory IDs, payment authority, fiscal authority, exit authorization, or gate state.");

        group.MapPost("/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId:guid}/decision", DecideServiceChannelReviewAsync)
            .WithName("DecideOperatorConsoleServiceChannelStatutoryDiscountReview")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleCanonicalStatutoryReviewDecisionRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(DecisionMutatePolicy))
            .WithSummary("Approve or reject a service-channel statutory discount decision")
            .WithDescription("Completes the canonical Central PMS decision created for a request originating from WebPay or Assisted Payment Terminal. Reviewer identity, timestamp, and authority are server-owned. Operator Console never calls either originating channel and never applies payable basis itself.");

        group.MapPost("/statutory-discounts/draft", DraftAsync)
            .WithName("DraftOperatorConsoleStatutoryDiscount")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountDraftRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountDraftResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleStatutoryDiscountDraftResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(DraftCreatePolicy))
            .WithSummary("Draft Operator Console statutory discount validation")
            .WithDescription("Validates and drafts a privacy-minimized statutory discount validation request after evaluating and persisting Operator Console access. When evidence is requested, this endpoint may persist metadata-only evidence reference records without image upload or raw evidence storage. This endpoint does not apply the discount or mutate payment, gate, coupon, provider, payable, settlement, or reconciliation state.");

        group.MapPost("/statutory-discounts/{draftId:guid}/decision", DecideAsync)
            .WithName("DecideOperatorConsoleStatutoryDiscount")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountDecisionRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleStatutoryDiscountDecisionResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(DecisionMutatePolicy))
            .WithSummary("Decide Operator Console statutory discount validation")
            .WithDescription("Approves or rejects an existing Operator Console statutory discount validation draft after evaluating and persisting Operator Console access. This endpoint only transitions validation decision status and does not apply the discount or mutate payment, gate, coupon, provider, payable, settlement, or reconciliation state.");

        group.MapPost("/statutory-discounts/{draftId:guid}/evidence", CaptureEvidenceAsync)
            .WithName("CaptureOperatorConsoleStatutoryDiscountEvidence")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountEvidenceCaptureRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountEvidenceCaptureResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceCapturePolicy))
            .WithSummary("Capture Operator Console statutory discount evidence metadata")
            .WithDescription("Captures metadata-only statutory discount evidence for an existing Operator Console validation draft. This endpoint stores no raw evidence bytes, performs no OCR or automated ID verification, and only updates evidence metadata and evidence satisfaction state.");

        group.MapGet("/statutory-discounts/{draftId:guid}/evidence", ListEvidenceAsync)
            .WithName("ListOperatorConsoleStatutoryDiscountEvidence")
            .WithTags("OperatorConsole")
            .Produces<OperatorConsoleStatutoryDiscountEvidenceListResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(EvidenceViewPolicy))
            .WithSummary("List Operator Console statutory discount evidence metadata")
            .WithDescription("Lists metadata-only statutory discount evidence records for an Operator Console validation draft. This endpoint does not return raw evidence, OCR data, raw ID numbers, or document verification results.");

        return app;
    }

    private static async Task<IResult> ListDraftsAsync(
        string? status,
        string? entitlementType,
        Guid? siteId,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        int? page,
        int? pageSize,
        Guid? correlationId,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountReadService service,
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ReadActivitySource.StartActivity("HTTP ListOperatorConsoleStatutoryDiscountDrafts", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", effectiveCorrelationId);
        activity?.SetTag("status", status);
        activity?.SetTag("entitlement_type", entitlementType);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);
            effectiveCorrelationId = identity.CorrelationId;

            var access = await EvaluateAndPersistAccessAsync(
                identity,
                OperatorConsoleActionCodes.ViewStatutoryDiscountDraft,
                ParkingSessionId: null,
                IdempotencyKey: $"operator-console-read-queue-{effectiveCorrelationId}",
                accessEvaluationService,
                accessEvaluationWriter,
                httpRequest);

            if (!access.Allowed)
            {
                return AccessDenied(access, effectiveCorrelationId);
            }

            var result = await service.ListDraftsAsync(
                new OperatorConsoleStatutoryDiscountDraftQueueQuery(
                    status,
                    entitlementType,
                    siteId ?? access.SiteContext.SiteId,
                    createdFrom,
                    createdTo,
                    page.GetValueOrDefault(1),
                    pageSize.GetValueOrDefault(25),
                    effectiveCorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("draft_count", result.Items.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_READ_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount queue read failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_READ_FAILED",
                    "The Operator Console statutory discount queue could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetDraftAsync(
        Guid draftId,
        Guid? correlationId,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountReadService service,
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ReadActivitySource.StartActivity("HTTP GetOperatorConsoleStatutoryDiscountDraft", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", effectiveCorrelationId);
        activity?.SetTag("statutory_discount_validation_id", draftId);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);
            effectiveCorrelationId = identity.CorrelationId;

            var result = await service.GetDraftAsync(
                new OperatorConsoleStatutoryDiscountDraftDetailQuery(draftId, effectiveCorrelationId),
                httpRequest.HttpContext.RequestAborted);

            if (result is null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.NotFound(BuildError(
                    "STATUTORY_DISCOUNT_DRAFT_NOT_FOUND",
                    "The Operator Console statutory discount draft was not found.",
                    effectiveCorrelationId));
            }

            var access = await EvaluateAndPersistAccessAsync(
                identity with
                {
                    SiteId = result.SiteId,
                    SiteGroupId = result.SiteGroupId
                },
                OperatorConsoleActionCodes.ViewStatutoryDiscountDraft,
                result.ParkingSessionId,
                $"operator-console-read-detail-{draftId}-{effectiveCorrelationId}",
                accessEvaluationService,
                accessEvaluationWriter,
                httpRequest);

            if (!access.Allowed)
            {
                return AccessDenied(access, effectiveCorrelationId);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_READ_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount detail read failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_READ_FAILED",
                    "The Operator Console statutory discount detail could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ListAuditReportAsync(
        Guid? siteId,
        Guid? siteGroupId,
        Guid? operatorUserId,
        Guid? parkingSessionId,
        string? validationStatus,
        string? evidenceStatus,
        string? accessDecision,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        int? offset,
        Guid? correlationId,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountReadService service,
        IOperatorConsoleAccessEvaluationService accessEvaluationService,
        IOperatorConsoleAccessEvaluationWriter accessEvaluationWriter,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ReadActivitySource.StartActivity("HTTP ListOperatorConsoleStatutoryDiscountAuditReport", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", effectiveCorrelationId);
        activity?.SetTag("validation_status", validationStatus);
        activity?.SetTag("evidence_status", evidenceStatus);

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
                OperatorConsoleActionCodes.ViewAuditReport,
                ParkingSessionId: parkingSessionId,
                IdempotencyKey: $"operator-console-audit-report-{effectiveCorrelationId}",
                accessEvaluationService,
                accessEvaluationWriter,
                httpRequest);

            if (!access.Allowed)
            {
                return AccessDenied(access, effectiveCorrelationId);
            }

            var result = await service.ListAuditReportAsync(
                new OperatorConsoleStatutoryDiscountAuditReportQuery(
                    siteId ?? access.SiteContext.SiteId,
                    siteGroupId ?? access.SiteContext.SiteGroupId,
                    operatorUserId,
                    parkingSessionId,
                    validationStatus,
                    evidenceStatus,
                    accessDecision,
                    from,
                    to,
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
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_AUDIT_REPORT_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount audit report read failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_AUDIT_REPORT_READ_FAILED",
                    "The Operator Console statutory discount audit report could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DraftAsync(
        OperatorConsoleStatutoryDiscountDraftRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountDraftService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP DraftOperatorConsoleStatutoryDiscount", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("parking_session_id", request.ParkingSessionId);
        activity?.SetTag("entitlement_type", request.EntitlementType);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.UserId,
                request.OperatorDeviceBindingId,
                request.OperatorShiftId,
                request.SiteId,
                request.SiteGroupId,
                request.CorrelationId);

            var result = await service.DraftAsync(
                new OperatorConsoleStatutoryDiscountDraftCommand(
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    request.ParkingSessionId,
                    request.TicketReference,
                    request.PlateNumber,
                    request.EntitlementType,
                    request.IdDocumentType,
                    request.IssuingAuthority,
                    request.ExpiryDate,
                    request.MaskedIdReference,
                    request.EntitlementFingerprint,
                    request.EvidenceCaptureRequested,
                    request.EvidenceAccessIntent,
                    request.OperatorAttestation,
                    request.AttestationNotes,
                    request.ReasonCode,
                    request.IdempotencyKey,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("draft_accepted", result.DraftAccepted);
            activity?.SetTag("draft_persisted", result.DraftPersisted);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console statutory discount draft completed. evaluation_id={EvaluationId} access_allowed={AccessAllowed} draft_accepted={DraftAccepted} draft_persisted={DraftPersisted}",
                result.AccessEvaluationId,
                result.AccessAllowed,
                result.DraftAccepted,
                result.DraftPersisted);

            var response = ToContract(result);
            return result.AccessAllowed && result.ErrorCode == "SESSION_NOT_FOUND"
                ? Results.NotFound(response)
                : Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DRAFT_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (OperatorConsoleStatutoryDiscountDraftAlreadyExistsException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.Conflict(BuildError("STATUTORY_DISCOUNT_DRAFT_ALREADY_EXISTS", ex.Message, request.CorrelationId));
        }
        catch (OperatorConsoleStatutoryDiscountDraftPolicyReferenceMissingException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(
                ex,
                "Operator Console statutory discount draft policy reference mapping missing for policy_code={PolicyCode} entitlement_type={EntitlementType}.",
                ex.PolicyCode,
                ex.EntitlementType);
            return Results.Conflict(BuildError(
                "STATUTORY_DISCOUNT_POLICY_REFERENCE_NOT_MAPPED",
                "The resolved statutory discount policy is not ready for draft persistence.",
                request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount draft failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DRAFT_FAILED",
                    "The Operator Console statutory discount draft could not be completed.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };

    private static OperatorConsoleStatutoryDiscountDraftQueueResponse ToContract(
        OperatorConsoleStatutoryDiscountDraftQueueResult result) =>
        new(
            result.Items.Select(item => new OperatorConsoleStatutoryDiscountDraftQueueItem(
                item.DraftId,
                item.ParkingSessionId,
                item.TicketReference,
                item.PlateNumber,
                item.SiteId,
                item.SiteName,
                item.EntitlementType,
                item.ValidationStatus,
                item.EvidenceRequired,
                item.EvidenceRequiredSatisfied,
                item.EvidenceCount,
                item.LatestEvidenceStatus,
                item.PolicyResolutionBasis,
                item.PolicyCode,
                item.PolicyName,
                item.OriginalAmountMinorUnits,
                item.PayableAmountMinorUnits,
                OperatorConsolePhpCurrency.RequireForAmounts(
                    item.CurrencyCode,
                    item.OriginalAmountMinorUnits,
                    item.PayableAmountMinorUnits),
                item.RequestedAt,
                item.RequestedByUserId,
                item.BlockedReason)).ToArray(),
            result.Page,
            result.PageSize,
            result.HasMore,
            result.CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftDetailResponse ToContract(
        OperatorConsoleStatutoryDiscountDraftDetailResult result) =>
        new(
            result.DraftId,
            result.ParkingSessionId,
            result.TicketReference,
            result.PlateNumber,
            result.SiteId,
            result.SiteName,
            result.SiteGroupId,
            result.EntitlementType,
            result.ValidationStatus,
            result.EvidenceRequired,
            result.EvidenceCaptured,
            result.EvidenceRequiredSatisfied,
            result.EvidenceCount,
            result.LatestEvidenceStatus,
            result.RequiredEvidenceTypes,
            result.RequestedAt,
            result.ValidatedAt,
            result.RequestedByUserId,
            result.ValidatedByUserId,
            result.DecisionReasonCode,
            result.FailureReasonCode,
            result.PolicyResolutionBasis,
            result.StatutoryDiscountPolicyId,
            result.ResolvedJurisdictionId,
            result.PolicyCode,
            result.PolicyName,
            result.LegalBasisReference,
            result.OrdinanceReference,
            result.NationalLawReference,
            result.VerificationStatus,
            result.BenefitType,
            result.FreeDurationMinutes,
            result.SucceedingHoursDiscountRule,
            result.DiscountBaseScope,
            result.StackingPolicy,
            result.PolicySnapshot,
            result.OriginalTariffSnapshotId,
            result.PayableBasisApplicationId,
            result.PayableBasisApplicationStatus,
            result.AppliedTariffSnapshotId,
            result.OriginalAmountMinorUnits,
            result.VatAmountMinorUnits,
            result.VatExclusiveAmountMinorUnits,
            result.StatutoryDiscountAmountMinorUnits,
            result.PayableAmountMinorUnits,
            result.FinalPayableAmountMinorUnits,
            OperatorConsolePhpCurrency.RequireForAmounts(
                result.CurrencyCode,
                result.OriginalAmountMinorUnits,
                result.VatAmountMinorUnits,
                result.VatExclusiveAmountMinorUnits,
                result.StatutoryDiscountAmountMinorUnits,
                result.PayableAmountMinorUnits,
                result.FinalPayableAmountMinorUnits),
            result.Activity,
            result.StatutoryDiscountDecisionCommandId,
            result.StatutoryDiscountPayableBasisApplicationCommandId);

    private static OperatorConsoleStatutoryDiscountAuditReportResponse ToContract(
        OperatorConsoleStatutoryDiscountAuditReportResult result) =>
        new(
            result.Items.Select(item => new OperatorConsoleStatutoryDiscountAuditReportItem(
                item.DraftId,
                item.DraftId,
                item.ParkingSessionId,
                item.TicketReference,
                item.PlateNumber,
                item.SiteId,
                item.SiteGroupId,
                item.EntitlementType,
                item.ValidationStatus,
                item.EvidenceRequired,
                item.EvidenceCaptured,
                item.EvidenceRequiredSatisfied,
                item.EvidenceCount,
                item.LatestEvidenceStatus,
                item.PayableBasisApplicationStatus,
                item.OriginalAmountMinorUnits,
                item.StatutoryDiscountAmountMinorUnits,
                item.FinalPayableAmountMinorUnits,
                OperatorConsolePhpCurrency.RequireForAmounts(
                    item.CurrencyCode,
                    item.OriginalAmountMinorUnits,
                    item.StatutoryDiscountAmountMinorUnits,
                    item.FinalPayableAmountMinorUnits),
                item.RequestedByUserId,
                item.ValidatedByUserId,
                item.RequestedAt,
                item.ValidatedAt,
                item.CorrelationId,
                item.PolicyCode,
                item.OrdinanceReference,
                item.LegalBasisReference,
                item.AppliedTariffSnapshotId,
                item.AccessEvaluationSummary)).ToArray(),
            result.TotalCount,
            result.Limit,
            result.Offset,
            result.CorrelationId);

    private static async Task<IResult> ListServiceChannelReviewsAsync(
        string? status,
        string? sourceChannel,
        string? entitlementType,
        Guid? siteId,
        Guid? siteGroupId,
        Guid? parkingSessionId,
        string? search,
        DateTimeOffset? submittedFrom,
        DateTimeOffset? submittedTo,
        int? page,
        int? pageSize,
        Guid? correlationId,
        HttpRequest httpRequest,
        IOperatorConsoleServiceChannelStatutoryDiscountReviewService service,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ServiceChannelReviewActivitySource.StartActivity("HTTP ListOperatorConsoleServiceChannelStatutoryDiscountReviews", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);
            effectiveCorrelationId = identity.CorrelationId;

            var result = await service.ListAsync(
                    new StatutoryDiscountServiceChannelReviewQueueQuery(
                        siteId,
                        siteGroupId,
                        sourceChannel,
                        entitlementType,
                        parkingSessionId,
                        status,
                        search,
                        submittedFrom,
                        submittedTo,
                        page.GetValueOrDefault(1),
                        pageSize.GetValueOrDefault(25),
                        effectiveCorrelationId),
                    ToReviewAccessContext(identity, httpRequest, $"operator-console-service-channel-review-list-{effectiveCorrelationId:N}"),
                    httpRequest.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (UnauthorizedAccessException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.Json(
                BuildError("OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_ACCESS_DENIED", "Operator Console service-channel statutory discount review access was denied.", effectiveCorrelationId),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console service-channel statutory discount review list failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_LIST_FAILED",
                    "The service-channel statutory discount review list could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetServiceChannelReviewAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid? correlationId,
        HttpRequest httpRequest,
        IOperatorConsoleServiceChannelStatutoryDiscountReviewService service,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = ServiceChannelReviewActivitySource.StartActivity("HTTP GetOperatorConsoleServiceChannelStatutoryDiscountReview", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);
            effectiveCorrelationId = identity.CorrelationId;

            var result = await service.GetAsync(
                    statutoryDiscountDecisionCommandId,
                    ToReviewAccessContext(identity, httpRequest, $"operator-console-service-channel-review-detail-{statutoryDiscountDecisionCommandId:N}"),
                    httpRequest.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result is null
                ? Results.NotFound(BuildError("STATUTORY_DISCOUNT_SERVICE_CHANNEL_REVIEW_NOT_FOUND", "The service-channel statutory discount review was not found.", effectiveCorrelationId))
                : Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (UnauthorizedAccessException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.Json(
                BuildError("OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_ACCESS_DENIED", "Operator Console service-channel statutory discount review access was denied.", effectiveCorrelationId),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console service-channel statutory discount review detail failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_DETAIL_FAILED",
                    "The service-channel statutory discount review detail could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DecideServiceChannelReviewAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleCanonicalStatutoryReviewDecisionRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleServiceChannelStatutoryDiscountReviewService service,
        IAntiforgery antiforgery,
        IOptions<CentralPmsRbacOptions> rbacOptions,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory)
    {
        using var activity = ServiceChannelReviewActivitySource.StartActivity("HTTP DecideOperatorConsoleServiceChannelStatutoryDiscountReview", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");
        var effectiveCorrelationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(httpRequest);

        try
        {
            if (request.AdditionalFields is { Count: > 0 })
            {
                throw new ArgumentException("Client-authored identity, authority, permission, role, reviewer, timestamp, Site, or Site Group fields are not accepted.");
            }
            await ValidateHumanSessionCsrfAsync(httpRequest, antiforgery).ConfigureAwait(false);
            RejectAuthenticatedAuthorityHeaders(httpRequest);
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);
            effectiveCorrelationId = identity.CorrelationId;

            var decisionPermission = await VerifyDecisionPermissionAsync(
                    httpRequest,
                    identity.UserId,
                    request.Decision,
                    effectiveCorrelationId,
                    rbacOptions.Value,
                    rbacRepository)
                .ConfigureAwait(false);
            if (decisionPermission is not null)
            {
                return decisionPermission;
            }

            var result = await service.DecideAsync(
                    new StatutoryDiscountServiceChannelReviewDecisionCommand(
                        statutoryDiscountDecisionCommandId,
                        identity.UserId,
                        identity.OperatorDeviceBindingId,
                        identity.SiteId,
                        identity.SiteGroupId,
                        identity.OperatorShiftId,
                        request.Decision,
                        request.DecisionReasonCode,
                        DecisionNotes: null,
                        request.ReviewerAttestation,
                        request.IdempotencyKey,
                        identity.CorrelationId),
                    ToReviewAccessContext(identity, httpRequest, request.IdempotencyKey),
                    httpRequest.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            var response = ToContract(result);
            return result.ErrorCode == "STATUTORY_DISCOUNT_DECISION_ALREADY_COMPLETED"
                ? Results.Conflict(BuildError(result.ErrorCode, "The statutory discount decision already has a conflicting terminal review result.", result.CorrelationId))
                : Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_DECISION_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(BuildError("CSRF_VALIDATION_FAILED", "The secure decision request could not be validated.", effectiveCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console service-channel statutory discount review decision failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_SERVICE_CHANNEL_REVIEW_DECISION_FAILED",
                    "The service-channel statutory discount review decision could not be completed.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OperatorConsoleReviewAccessContext ToReviewAccessContext(
        OperatorConsoleIdentityContext identity,
        HttpRequest request,
        string idempotencyKey) =>
        new(
            identity.UserId,
            identity.OperatorDeviceBindingId,
            identity.OperatorShiftId,
            identity.SiteId,
            identity.SiteGroupId,
            identity.CorrelationId,
            idempotencyKey)
        {
            AuthorizedSiteIds = ReadGuidClaims(request.HttpContext.User, "site_id", identity.SiteId),
            AuthorizedSiteGroupIds = ReadGuidClaims(request.HttpContext.User, "site_group_id", identity.SiteGroupId),
            HasGlobalScope = string.Equals(request.HttpContext.User.FindFirst("has_global_scope")?.Value, "true", StringComparison.OrdinalIgnoreCase)
        };

    private static Guid[] ReadGuidClaims(ClaimsPrincipal principal, string claimType, Guid? fixtureFallback) =>
        principal.Claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))
            .Select(claim => Guid.TryParse(claim.Value, out var value) ? value : Guid.Empty)
            .Where(value => value != Guid.Empty)
            .Append(fixtureFallback.GetValueOrDefault())
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToArray();

    private static async Task ValidateHumanSessionCsrfAsync(HttpRequest request, IAntiforgery antiforgery)
    {
        if (string.Equals(request.HttpContext.User.Identity?.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
        {
            await antiforgery.ValidateRequestAsync(request.HttpContext).ConfigureAwait(false);
        }
    }

    private static void RejectAuthenticatedAuthorityHeaders(HttpRequest request)
    {
        if (request.HttpContext.User.Identity?.IsAuthenticated != true) return;
        string[] prohibited = ["X-Operator-User-Id", "X-Operator-Device-Binding-Id", "X-Operator-Shift-Id", "X-Site-Id", "X-Site-Group-Id", "X-Permissions", "X-Roles", "X-Reviewer-Id"];
        if (prohibited.Any(name => request.Headers.ContainsKey(name)))
        {
            throw new ArgumentException("Client-authored identity, authority, permission, role, reviewer, Site, or Site Group headers are not accepted.");
        }
    }

    private static OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse ToContract(
        StatutoryDiscountServiceChannelReviewQueueResult result) =>
        new(
            result.Items.Select(item => new OperatorConsoleServiceChannelStatutoryDiscountReviewQueueItem(
                item.StatutoryDiscountDecisionCommandId,
                item.RequestReference,
                item.ParkingSessionId,
                item.SourceChannel,
                item.SiteId,
                item.SiteGroupId,
                item.TicketReference,
                item.EntitlementType,
                item.CommandStatus,
                item.DecisionResultStatus,
                item.ReviewStatus,
                item.EvidenceRequired,
                item.EvidenceRecorded,
                item.OriginalTariffSnapshotId,
                item.SubmittedAt,
                item.CorrelationId)).ToArray(),
            result.TotalCount,
            result.Page,
            result.PageSize,
            result.HasMore,
            result.CorrelationId);

    private static OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse ToContract(
        StatutoryDiscountServiceChannelReviewDetail result) =>
        new(
            result.StatutoryDiscountDecisionCommandId,
            result.StatutoryDiscountValidationId,
            result.RequestReference,
            result.ParkingSessionId,
            result.SourceChannel,
            result.SiteId,
            result.SiteGroupId,
            result.TicketReference,
            result.PlateNumber,
            result.EntitlementType,
            result.CommandStatus,
            result.DecisionResultStatus,
            result.ReviewStatus,
            result.ReviewStatus switch
            {
                StatutoryDiscountServiceChannelReviewStatuses.Approved => "ELIGIBLE",
                StatutoryDiscountServiceChannelReviewStatuses.Rejected => "NOT_ELIGIBLE",
                _ => "PENDING_REVIEW"
            },
            result.PayableBasisApplicationStatus is null ? "NOT_YET_CREATED" : "CREATED",
            result.IdDocumentType,
            result.IssuingAuthority,
            result.ExpiryDate,
            result.MaskedIdReference,
            result.EvidenceReferences.Select(evidence => new OperatorConsoleServiceChannelStatutoryDiscountReviewEvidenceReference(
                    evidence.EvidenceType,
                    evidence.CaptureMethod,
                    evidence.ReferenceNumberMasked,
                    evidence.VerificationStatus))
                .ToArray(),
            result.RequesterAttestation,
            result.AttestationNotes,
            result.ReasonCode,
            result.EvidenceRequired,
            result.EvidenceRecorded,
            result.OriginalTariffSnapshotId,
            result.OriginalAmountMinorUnits,
            result.VatExclusiveAmountMinorUnits,
            result.VatAmountMinorUnits,
            result.StatutoryDiscountAmountMinorUnits,
            result.FinalPayableAmountMinorUnits,
            OperatorConsolePhpCurrency.RequireForAmounts(
                result.Currency,
                result.OriginalAmountMinorUnits,
                result.VatExclusiveAmountMinorUnits,
                result.VatAmountMinorUnits,
                result.StatutoryDiscountAmountMinorUnits,
                result.FinalPayableAmountMinorUnits),
            result.GoverningPolicy is null
                ? null
                : new OperatorConsoleServiceChannelStatutoryDiscountReviewPolicyAuthority(
                    result.GoverningPolicy.StatutoryDiscountPolicyVersionId,
                    result.GoverningPolicy.JurisdictionId,
                    result.GoverningPolicy.JurisdictionCode,
                    result.GoverningPolicy.JurisdictionDisplayName,
                    result.GoverningPolicy.PolicyCode,
                    result.GoverningPolicy.PolicyVersion,
                    result.GoverningPolicy.OrdinanceNumber,
                    result.GoverningPolicy.OrdinanceTitle,
                    result.GoverningPolicy.SourceVerificationStatus,
                    result.GoverningPolicy.TransactionPublicationStatus,
                    result.GoverningPolicy.DetailedRuleVerificationStatus,
                    result.GoverningPolicy.ParkingServiceApplicability,
                    result.GoverningPolicy.BenefitType,
                    result.GoverningPolicy.BeneficiaryResidencyScope,
                    result.GoverningPolicy.OfficialSourceAvailable,
                    result.GoverningPolicy.OrdinanceTextAvailable,
                    result.GoverningPolicy.OrdinanceNumberAvailable,
                    result.GoverningPolicy.EffectiveFrom,
                    result.GoverningPolicy.EffectiveTo,
                    result.GoverningPolicy.RequiredEvidenceTypes.Select(requirement => new StatutoryDiscountPolicyEvidenceRequirementDto(
                            requirement.EvidenceType,
                            requirement.RequirementStatus,
                            requirement.SafeRequirementLabel,
                            requirement.SafeRequirementNotes))
                        .ToArray(),
                    result.GoverningPolicy.LegalApprovabilityReason),
            result.ReviewerUserId,
            result.ReviewerAccessEvaluationId,
            result.ReviewerDecision,
            result.ReviewerReasonCode,
            result.SubmittedAt,
            result.ReviewedAt,
            result.PayableBasisApplicationStatus,
            result.CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionResponse ToContract(
        StatutoryDiscountServiceChannelReviewDecisionResult result) =>
        new(
            result.AccessEvaluationId,
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            result.DecisionAccepted,
            result.DecisionPersisted,
            DraftId: null,
            result.ParkingSessionId == Guid.Empty ? null : result.ParkingSessionId,
            result.EntitlementType,
            result.PreviousDecisionResultStatus,
            result.CurrentDecisionResultStatus,
            result.Decision,
            result.DecisionReasonCode,
            result.AlreadyDecided,
            result.DecisionChanged,
            result.IneligibilityReason,
            result.ErrorCode,
            result.CorrelationId,
            result.StatutoryDiscountDecisionCommandId);

    private static async Task<IResult> DecideAsync(
        Guid draftId,
        OperatorConsoleStatutoryDiscountDecisionRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountDecisionService service,
        IOptions<CentralPmsRbacOptions> rbacOptions,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory)
    {
        using var activity = DecisionActivitySource.StartActivity("HTTP DecideOperatorConsoleStatutoryDiscount", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("statutory_discount_validation_id", draftId);
        activity?.SetTag("decision", request.Decision);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.UserId,
                request.OperatorDeviceBindingId,
                request.OperatorShiftId,
                request.SiteId,
                request.SiteGroupId,
                request.CorrelationId);

            var decisionPermission = await VerifyDecisionPermissionAsync(
                    httpRequest,
                    identity.UserId,
                    request.Decision,
                    request.CorrelationId,
                    rbacOptions.Value,
                    rbacRepository)
                .ConfigureAwait(false);
            if (decisionPermission is not null)
            {
                return decisionPermission;
            }

            var result = await service.DecideAsync(
                new OperatorConsoleStatutoryDiscountDecisionCommand(
                    draftId,
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    request.Decision,
                    request.DecisionReasonCode,
                    request.DecisionNotes,
                    request.ReviewerAttestation,
                    request.IdempotencyKey,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("decision_accepted", result.DecisionAccepted);
            activity?.SetTag("decision_persisted", result.DecisionPersisted);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console statutory discount decision completed. evaluation_id={EvaluationId} access_allowed={AccessAllowed} decision_accepted={DecisionAccepted} decision_persisted={DecisionPersisted}",
                result.AccessEvaluationId,
                result.AccessAllowed,
                result.DecisionAccepted,
                result.DecisionPersisted);

            var response = ToContract(result);
            return result.AccessAllowed && result.ErrorCode == "DRAFT_NOT_FOUND"
                ? Results.NotFound(response)
                : Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (OperatorConsoleStatutoryDiscountDecisionConflictException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.Conflict(BuildError("STATUTORY_DISCOUNT_DRAFT_ALREADY_DECIDED", ex.Message, request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount decision failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_DECISION_FAILED",
                    "The Operator Console statutory discount decision could not be completed.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OperatorConsoleStatutoryDiscountDraftResponse ToContract(
        OperatorConsoleStatutoryDiscountDraftResult result) =>
        new(
            result.AccessEvaluationId,
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            result.DraftAccepted,
            result.DraftPersisted,
            result.DraftId,
            result.ParkingSessionId,
            result.EntitlementType,
            result.ValidationStatus,
            result.EvidenceCaptureRequired,
            result.EvidenceRequired,
            result.EvidenceReferenceCreated,
            result.EvidenceReferenceId,
            result.ReusedExistingDraft,
            result.Policy?.StatutoryDiscountPolicyId,
            result.Policy?.JurisdictionId,
            result.Policy?.PolicyResolutionBasis,
            result.Policy?.PolicyCode,
            result.Policy?.PolicyName,
            result.Policy?.LegalBasisReference,
            result.Policy?.OrdinanceReference,
            result.Policy?.NationalLawReference,
            result.Policy?.VerificationStatus,
            result.Policy?.BenefitType,
            result.Policy?.FreeDurationMinutes,
            result.Policy?.SucceedingHoursDiscountRule,
            result.Policy?.DiscountBaseScope,
            result.Policy?.StackingPolicy,
            result.Policy?.PolicySnapshot,
            result.IneligibilityReason,
            result.ErrorCode,
            result.CorrelationId,
            result.PolicyReadinessClassification,
            result.RequiresManualReview,
            result.PolicyReadinessReason,
            result.OperatorMessage);

    private static OperatorConsoleStatutoryDiscountDecisionResponse ToContract(
        OperatorConsoleStatutoryDiscountDecisionResult result) =>
        new(
            result.AccessEvaluationId,
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            result.DecisionAccepted,
            result.DecisionPersisted,
            result.DraftId,
            result.ParkingSessionId,
            result.EntitlementType,
            result.PreviousValidationStatus,
            result.CurrentValidationStatus,
            result.Decision,
            result.DecisionReasonCode,
            result.AlreadyDecided,
            result.DecisionChanged,
            result.IneligibilityReason,
            result.ErrorCode,
            result.CorrelationId,
            result.StatutoryDiscountDecisionCommandId);

    private static async Task<IResult> CaptureEvidenceAsync(
        Guid draftId,
        OperatorConsoleStatutoryDiscountEvidenceCaptureRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountEvidenceService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = EvidenceActivitySource.StartActivity("HTTP CaptureOperatorConsoleStatutoryDiscountEvidence", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("statutory_discount_validation_id", draftId);
        activity?.SetTag("evidence_type", request.EvidenceType);
        activity?.SetTag("capture_method", request.CaptureMethod);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                httpRequest,
                request.UserId,
                request.OperatorDeviceBindingId,
                request.OperatorShiftId,
                request.SiteId,
                request.SiteGroupId,
                request.CorrelationId);

            var result = await service.CaptureAsync(
                new OperatorConsoleStatutoryDiscountEvidenceCaptureCommand(
                    draftId,
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    request.EvidenceType,
                    request.CaptureMethod,
                    request.FileName,
                    request.ContentType,
                    request.SizeBytes,
                    request.StorageReference,
                    request.ReferenceNumber,
                    request.Notes,
                    request.OperatorConfirmation,
                    request.IdempotencyKey,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            if (result is null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.NotFound(BuildError(
                    "STATUTORY_DISCOUNT_DRAFT_NOT_FOUND",
                    "The Operator Console statutory discount draft was not found.",
                    request.CorrelationId));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount evidence capture failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_CAPTURE_FAILED",
                    "The Operator Console statutory discount evidence metadata could not be captured.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ListEvidenceAsync(
        Guid draftId,
        Guid? correlationId,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountEvidenceService service,
        ILoggerFactory loggerFactory)
    {
        var effectiveCorrelationId = correlationId.GetValueOrDefault(Guid.NewGuid());
        using var activity = EvidenceActivitySource.StartActivity("HTTP ListOperatorConsoleStatutoryDiscountEvidence", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", effectiveCorrelationId);
        activity?.SetTag("statutory_discount_validation_id", draftId);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(httpRequest, fallbackCorrelationId: effectiveCorrelationId);

            var result = await service.ListAsync(
                new OperatorConsoleStatutoryDiscountEvidenceListQuery(
                    draftId,
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            if (result is null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.NotFound(BuildError(
                    "STATUTORY_DISCOUNT_DRAFT_NOT_FOUND",
                    "The Operator Console statutory discount draft was not found.",
                    effectiveCorrelationId));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToContract(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_REQUEST", ex.Message, effectiveCorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount evidence list failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_LIST_FAILED",
                    "The Operator Console statutory discount evidence metadata could not be loaded.",
                    effectiveCorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureResponse ToContract(
        OperatorConsoleStatutoryDiscountEvidenceCaptureResult result) =>
        new(
            result.EvidenceId,
            result.DraftId,
            result.EvidenceType,
            result.CaptureMethod,
            result.FileName,
            result.ContentType,
            result.SizeBytes,
            result.StorageReference,
            result.ReferenceNumberMasked,
            result.CapturedByUserId,
            result.CapturedAt,
            result.RedactionStatus,
            result.VerificationStatus,
            result.EvidenceRequiredSatisfied,
            result.CurrentDraftStatus,
            result.AccessAllowed,
            result.ErrorCode,
            result.CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceListResponse ToContract(
        OperatorConsoleStatutoryDiscountEvidenceListResult result) =>
        new(
            result.DraftId,
            result.EvidenceRequired,
            result.EvidenceRequiredSatisfied,
            result.RequiredEvidenceTypes,
            result.EvidenceCount,
            result.LatestEvidenceStatus,
            result.Items.Select(item => new OperatorConsoleStatutoryDiscountEvidenceItem(
                item.EvidenceId,
                item.DraftId,
                    item.EvidenceType,
                    item.CaptureMethod,
                    item.StorageReference,
                    item.CapturedByUserId,
                    item.CapturedAt,
                item.RedactionStatus,
                item.VerificationStatus,
                item.CorrelationId)).ToArray(),
            result.CorrelationId);

    private static async Task<OperatorConsoleAccessEvaluationResult> EvaluateAndPersistAccessAsync(
        OperatorConsoleIdentityContext identity,
        string actionCode,
        Guid? ParkingSessionId,
        string IdempotencyKey,
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
                actionCode,
                ParkingSessionId,
                EvidenceAccessIntent: null,
                IdempotencyKey,
                identity.CorrelationId),
            httpRequest.HttpContext.RequestAborted);

        return await accessEvaluationWriter.PersistAsync(evaluation, httpRequest.HttpContext.RequestAborted);
    }

    private static IResult AccessDenied(OperatorConsoleAccessEvaluationResult access, Guid correlationId) =>
        Results.Json(
            BuildError(
                "OPERATOR_CONSOLE_ACCESS_DENIED",
                "Access denied for this Operator Console action.",
                correlationId),
            statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult?> VerifyDecisionPermissionAsync(
        HttpRequest request,
        Guid operatorUserId,
        string decision,
        Guid correlationId,
        CentralPmsRbacOptions options,
        ICentralPmsRbacRepository repository)
    {
        if (!options.Enabled)
        {
            return null;
        }

        if (ResolveGuid(request, CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, "service_identity_id", "client_id") is not null)
        {
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_HUMAN_REVIEWER_REQUIRED",
                    "A human Operator Console reviewer is required for statutory discount approval or rejection.",
                    correlationId),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var requiredPermission = NormalizeDecisionPermission(decision);
        if (requiredPermission is null)
        {
            return null;
        }

        if (ReadPermissions(request.HttpContext, options).Contains(requiredPermission) ||
            await repository.UserHasAnyPermissionAsync(operatorUserId, [requiredPermission], request.HttpContext.RequestAborted)
                .ConfigureAwait(false))
        {
            return null;
        }

        return Results.Json(
            BuildError(
                "OPERATOR_CONSOLE_DECISION_PERMISSION_REQUIRED",
                requiredPermission == ApprovePermission
                    ? "Approval requires statutory discount approve authority."
                    : "Rejection requires statutory discount reject authority.",
                correlationId),
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static string? NormalizeDecisionPermission(string decision) =>
        string.IsNullOrWhiteSpace(decision)
            ? null
            : decision.Trim().ToUpperInvariant() switch
            {
                "APPROVE" => ApprovePermission,
                "REJECT" => RejectPermission,
                _ => null
            };

    private static IReadOnlySet<string> ReadPermissions(HttpContext context, CentralPmsRbacOptions options)
    {
        var permissions = context.User.Claims
            .Where(claim => string.Equals(claim.Type, CentralPmsRbacPolicyCatalog.PermissionClaimType, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "scope", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (options.AllowPermissionHeader &&
            context.Request.Headers.TryGetValue(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, out var headerValue))
        {
            foreach (var permission in headerValue.ToString()
                         .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                permissions.Add(permission);
            }
        }

        return permissions;
    }

    private static Guid? ResolveGuid(HttpRequest request, string headerName, params string[] claimTypes)
    {
        if (request.Headers.TryGetValue(headerName, out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerGuid) &&
            headerGuid != Guid.Empty)
        {
            return headerGuid;
        }

        foreach (var claimType in claimTypes)
        {
            if (Guid.TryParse(request.HttpContext.User.FindFirstValue(claimType), out var claimGuid) &&
                claimGuid != Guid.Empty)
            {
                return claimGuid;
            }
        }

        return null;
    }
}
