using System.Diagnostics;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console statutory discount validation draft endpoint.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This endpoint persists Operator Console access evaluation evidence before draft creation.
/// - This endpoint may persist a privacy-minimized statutory discount validation draft and metadata-only evidence reference.
/// - This endpoint may persist a review decision status transition on an existing validation draft.
/// - This endpoint may apply an approved statutory discount validation to payable basis by creating a superseding tariff snapshot and immutable application record.
/// - This endpoint never mutates PaymentAttempt, PaymentConfirmation,
///   ExitAuthorization, provider outcome, gate consume, coupon application, settlement truth,
///   reconciliation records, or payment finality.
/// </summary>
public static class OperatorConsoleStatutoryDiscountDraftEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraft");
    private static readonly ActivitySource DecisionActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDecision");
    private static readonly ActivitySource ApplyPayableBasisActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountApplyPayableBasis");

    /// <summary>
    /// Maps Operator Console statutory discount draft endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleStatutoryDiscountDraftEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapPost("/statutory-discounts/draft", DraftAsync)
            .WithName("DraftOperatorConsoleStatutoryDiscount")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountDraftRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountDraftResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleStatutoryDiscountDraftResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
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
            .WithSummary("Decide Operator Console statutory discount validation")
            .WithDescription("Approves or rejects an existing Operator Console statutory discount validation draft after evaluating and persisting Operator Console access. This endpoint only transitions validation decision status and does not apply the discount or mutate payment, gate, coupon, provider, payable, settlement, or reconciliation state.");

        group.MapPost("/statutory-discounts/{validationId:guid}/apply-payable-basis", ApplyPayableBasisAsync)
            .WithName("ApplyOperatorConsoleStatutoryDiscountPayableBasis")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountApplyPayableBasisRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleStatutoryDiscountApplyPayableBasisResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithSummary("Apply Operator Console statutory discount payable basis")
            .WithDescription("Applies an already-approved Operator Console statutory discount validation to payable basis after evaluating and persisting Operator Console access. This endpoint creates statutory discount payable-basis application evidence and a superseding tariff snapshot only; it does not create payment attempts, confirm payment, call providers, issue exit authorization, open gates, create coupons, or create reconciliation records.");

        return app;
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
            var result = await service.DraftAsync(
                new OperatorConsoleStatutoryDiscountDraftCommand(
                    request.UserId,
                    request.OperatorDeviceBindingId,
                    request.SiteId,
                    request.SiteGroupId,
                    request.OperatorShiftId,
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
                    request.CorrelationId),
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
            var result = await service.DecideAsync(
                new OperatorConsoleStatutoryDiscountDecisionCommand(
                    draftId,
                    request.UserId,
                    request.OperatorDeviceBindingId,
                    request.SiteId,
                    request.SiteGroupId,
                    request.OperatorShiftId,
                    request.Decision,
                    request.DecisionReasonCode,
                    request.DecisionNotes,
                    request.ReviewerAttestation,
                    request.IdempotencyKey,
                    request.CorrelationId),
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
            result.CorrelationId);

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
            var result = await service.ApplyAsync(
                new OperatorConsoleStatutoryDiscountApplyPayableBasisCommand(
                    validationId,
                    request.UserId,
                    request.OperatorDeviceBindingId,
                    request.SiteId,
                    request.SiteGroupId,
                    request.OperatorShiftId,
                    request.OriginalTariffSnapshotId,
                    request.IdempotencyKey,
                    request.CorrelationId),
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
            result.IneligibilityReason,
            result.ErrorCode,
            result.CorrelationId);
}
