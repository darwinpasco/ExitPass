using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console statutory discount validation draft endpoint.
///
/// ExitPass v1.3 Invariants Enforced:
/// - This endpoint persists Operator Console access evaluation evidence before draft creation.
/// - This endpoint may persist a privacy-minimized statutory discount validation draft and metadata-only evidence reference.
/// - This endpoint may persist a review decision status transition on an existing validation draft.
/// - This endpoint may apply an approved statutory discount validation to payable basis by creating immutable application evidence.
/// - This endpoint never mutates PaymentAttempt, PaymentConfirmation,
///   ExitAuthorization, provider outcome, gate consume, coupon application, settlement truth,
///   reconciliation records, or payment finality.
/// </summary>
public static class OperatorConsoleStatutoryDiscountDraftEndpoints
{
    private const string WorkflowCode = OperatorConsoleActionCodes.StatutoryDiscountValidationWorkflow;
    private const string DraftViewPolicy = "OperatorConsoleStatutoryDiscountDraftView";
    private const string DraftCreatePolicy = "OperatorConsoleStatutoryDiscountDraftCreate";
    private const string DecisionPolicy = "OperatorConsoleStatutoryDiscountDecisionReview";
    private const string EvidenceCapturePolicy = "OperatorConsoleStatutoryDiscountEvidenceCapture";
    private const string EvidenceViewPolicy = "OperatorConsoleStatutoryDiscountEvidenceView";
    private const string ApplyPayableBasisPolicy = "OperatorConsoleStatutoryDiscountPayableBasisApply";
    private const string AuditReadPolicy = "OperatorConsoleStatutoryDiscountAuditRead";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraft");
    private static readonly ActivitySource ReadActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountRead");
    private static readonly ActivitySource DecisionActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDecision");
    private static readonly ActivitySource ApplyPayableBasisActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountApplyPayableBasis");
    private static readonly ActivitySource EvidenceActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountEvidence");

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
            .WithMetadata(new ReconciliationPolicyMetadata(DecisionPolicy))
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

        group.MapPost("/statutory-discounts/{validationId:guid}/apply-payable-basis", ApplyPayableBasisAsync)
            .WithName("ApplyOperatorConsoleStatutoryDiscountPayableBasis")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountApplyPayableBasisRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(ApplyPayableBasisPolicy))
            .WithSummary("Apply Operator Console statutory discount payable basis")
            .WithDescription("Applies an already-approved Operator Console statutory discount validation to payable basis after evaluating and persisting Operator Console access. This endpoint uses the policy snapshot persisted on the validation and may create an applied tariff snapshot plus statutory discount payable-basis application evidence; it does not create payment attempts, confirm payment, call providers, issue exit authorization, open gates, create coupons, or create reconciliation records.");

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
                item.CurrencyCode,
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
            result.CurrencyCode,
            result.Activity,
            result.StatutoryDiscountDecisionCommandId);

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
                item.CurrencyCode,
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

    private static async Task<IResult> DecideAsync(
        Guid draftId,
        OperatorConsoleStatutoryDiscountDecisionRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountDecisionService service,
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

    private static async Task<IResult> ApplyPayableBasisAsync(
        Guid validationId,
        OperatorConsoleStatutoryDiscountApplyPayableBasisRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ApplyPayableBasisActivitySource.StartActivity("HTTP ApplyOperatorConsoleStatutoryDiscountPayableBasis", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraftEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("statutory_discount_validation_id", validationId);

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

            var result = await service.ApplyAsync(
                new OperatorConsoleStatutoryDiscountApplyPayableBasisCommand(
                    validationId,
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    request.OriginalTariffSnapshotId,
                    request.IdempotencyKey,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("application_accepted", result.ApplicationAccepted);
            activity?.SetTag("application_persisted", result.ApplicationPersisted);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console statutory discount payable-basis application completed. evaluation_id={EvaluationId} access_allowed={AccessAllowed} application_accepted={ApplicationAccepted} application_persisted={ApplicationPersisted}",
                result.AccessEvaluationId,
                result.AccessAllowed,
                result.ApplicationAccepted,
                result.ApplicationPersisted);

            var response = ToContract(result);
            return result.AccessAllowed && result.ErrorCode == "STATUTORY_DISCOUNT_VALIDATION_NOT_FOUND"
                ? Results.NotFound(response)
                : Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_STATUTORY_DISCOUNT_APPLY_PAYABLE_BASIS_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount payable-basis application failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_APPLY_PAYABLE_BASIS_FAILED",
                    "The Operator Console statutory discount payable-basis application could not be completed.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisResponse ToContract(
        OperatorConsoleStatutoryDiscountApplyPayableBasisResult result) =>
        new(
            result.AccessEvaluationId,
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            result.ApplicationAccepted,
            result.ApplicationPersisted,
            result.PayableBasisApplicationId,
            result.StatutoryDiscountValidationId,
            result.ParkingSessionId,
            result.OriginalTariffSnapshotId,
            result.AppliedTariffSnapshotId,
            result.ApplicationStatus,
            result.AlreadyApplied,
            result.GrossAmountMinorUnits,
            result.VatAmountMinorUnits,
            result.VatExclusiveAmountMinorUnits,
            result.StatutoryDiscountAmountMinorUnits,
            result.FinalPayableAmountMinorUnits,
            result.CurrencyCode,
            result.StatutoryDiscountPolicyId,
            result.ResolvedJurisdictionId,
            result.PolicyResolutionBasis,
            result.PolicyCode,
            result.BenefitType,
            result.NationalLawReference,
            result.OrdinanceReference,
            result.PolicySnapshotUsed,
            result.IneligibilityReason,
            result.ErrorCode,
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
}
