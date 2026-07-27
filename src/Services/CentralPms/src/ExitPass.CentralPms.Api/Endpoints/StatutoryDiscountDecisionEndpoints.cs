using System.Diagnostics;
using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Shared channel-neutral Central PMS statutory-discount decision and readback endpoints.
/// </summary>
public static class StatutoryDiscountDecisionEndpoints
{
    private const string SubmitPolicy = "CentralPmsStatutoryDiscountDecisionSubmit";
    private const string ReadPolicy = "CentralPmsStatutoryDiscountDecisionRead";

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.StatutoryDiscountDecision");

    public static IEndpointRouteBuilder MapStatutoryDiscountDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/statutory-discounts/decisions")
            .WithTags("StatutoryDiscounts");

        group.MapPost("", SubmitAsync)
            .WithName("SubmitStatutoryDiscountDecision")
            .WithMetadata(new ReconciliationPolicyMetadata(SubmitPolicy))
            .Produces<StatutoryDiscountDecisionResponse>(StatusCodes.Status201Created)
            .Produces<StatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/{statutoryDiscountDecisionCommandId:guid}", ReadAsync)
            .WithName("GetStatutoryDiscountDecision")
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<StatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> SubmitAsync(
        HttpRequest request,
        StatutoryDiscountDecisionRequest? body,
        IStatutoryDiscountDecisionFacadeService service,
        IOptions<CentralPmsRbacOptions> rbacOptions,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SubmitStatutoryDiscountDecision", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/statutory-discounts/decisions");
        activity?.SetTag("requested_source_channel", body?.SourceChannel);
        activity?.SetTag("parking_session_id", body?.ParkingSessionId);

        if (body is null)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "Request body is required.", Guid.Empty));
        }

        if (!TryReadHeaders(request, out var idempotencyKey, out var correlationId, out var headerError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, headerError!.Message);
            return Results.BadRequest(headerError);
        }

        var requestedSourceChannel = StatutoryDiscountSourceChannels.Normalize(body.SourceChannel);
        if (!StatutoryDiscountSourceChannels.IsSupported(requestedSourceChannel))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unsupported source channel.");
            return Results.BadRequest(BuildError(
                "UNSUPPORTED_SOURCE_CHANNEL",
                "Source channel must be OPERATOR_CONSOLE, WEBPAY, or ASSISTED_PAYMENT_TERMINAL.",
                correlationId));
        }

        if (!TryResolveAuthenticatedSourceChannel(
                request.HttpContext,
                rbacOptions.Value,
                out var effectiveSourceChannel,
                out var actorId,
                out var channelError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Source channel permission is missing or ambiguous.");
            return Results.Json(
                BuildError(channelError!.ErrorCode, channelError.Message, correlationId, channelError.Details),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(requestedSourceChannel, effectiveSourceChannel, StringComparison.Ordinal))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Requested source channel does not match authenticated channel.");
            return Results.Json(
                BuildError(
                    "CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH",
                    "The request source channel must match the authenticated channel identity.",
                    correlationId),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!ValidateChannelFieldMatrix(body, effectiveSourceChannel, actorId, out var matrixError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Source-channel request field validation failed.");
            return Results.BadRequest(BuildError(matrixError!.ErrorCode, matrixError.Message, correlationId, matrixError.Details));
        }

        try
        {
            var result = await service.SubmitAsync(
                    ToCommand(body, effectiveSourceChannel, actorId, idempotencyKey!, correlationId),
                    cancellationToken)
                .ConfigureAwait(false);
            var response = ToResponse(result);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("statutory_discount_decision_command_id", result.StatutoryDiscountDecisionCommandId);
            activity?.SetTag("statutory_discount_validation_id", result.StatutoryDiscountValidationId);
            activity?.SetTag("decision_status", result.DecisionStatus);

            return string.Equals(result.ResultClassification, "IDEMPOTENT_REPLAY", StringComparison.Ordinal)
                ? Results.Ok(response)
                : Results.Created($"/v1/statutory-discounts/decisions/{result.StatutoryDiscountDecisionCommandId}", response);
        }
        catch (StatutoryDiscountDecisionRejectedException ex) when (IsConflict(ex.ErrorCode))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.Conflict(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (StatutoryDiscountDecisionRejectedException ex) when (ex.IsNotFound)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.NotFound(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (StatutoryDiscountDecisionRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
    }

    private static async Task<IResult> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        HttpRequest request,
        IStatutoryDiscountDecisionFacadeService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetStatutoryDiscountDecision", ActivityKind.Server);
        activity?.SetTag("http.route", "GET /v1/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}");
        activity?.SetTag("statutory_discount_decision_command_id", statutoryDiscountDecisionCommandId);

        if (!TryReadCorrelationId(request, out var correlationId, out var headerError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, headerError!.Message);
            return Results.BadRequest(headerError);
        }

        try
        {
            var result = await service.GetAsync(statutoryDiscountDecisionCommandId, correlationId, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return Results.NotFound(BuildError(
                    "STATUTORY_DISCOUNT_DECISION_NOT_FOUND",
                    "Statutory discount decision was not found.",
                    correlationId));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok(ToResponse(result));
        }
        catch (StatutoryDiscountDecisionRejectedException ex) when (ex.IsNotFound)
        {
            return Results.NotFound(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (StatutoryDiscountDecisionRejectedException ex)
        {
            return Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
    }

    private static StatutoryDiscountDecisionCommand ToCommand(
        StatutoryDiscountDecisionRequest body,
        string effectiveSourceChannel,
        Guid actorId,
        string idempotencyKey,
        Guid correlationId) =>
        new(
            body.RequestReference,
            effectiveSourceChannel,
            body.ParkingSessionId,
            body.SiteId,
            body.SiteGroupId,
            body.TicketReference,
            body.PlateNumber,
            body.EntitlementType,
            body.IdDocumentType,
            body.IssuingAuthority,
            body.ExpiryDate,
            body.MaskedIdReference,
            body.EvidenceCaptureRequested,
            (body.EvidenceReferences ?? [])
                .Select(evidence => new StatutoryDiscountEvidenceReference(
                    evidence.EvidenceType,
                    evidence.CaptureMethod,
                    evidence.FileName,
                    evidence.ContentType,
                    evidence.SizeBytes,
                    evidence.StorageReference,
                    evidence.ReferenceNumberMasked,
                    evidence.VerificationStatus))
                .ToArray(),
            actorId,
            body.OperatorDeviceBindingId,
            body.OperatorShiftId,
            body.RequesterAttestation,
            body.AttestationNotes,
            body.ReasonCode,
            body.Decision,
            body.DecisionReasonCode,
            body.ReviewerUserId,
            body.ReviewerAttestation,
            body.ApplyPayableBasis,
            body.OriginalTariffSnapshotId,
            idempotencyKey,
            correlationId);

    private static StatutoryDiscountDecisionResponse ToResponse(StatutoryDiscountDecisionResult result) =>
        new(
            result.StatutoryDiscountDecisionCommandId,
            result.RequestReference,
            result.StatutoryDiscountValidationId,
            result.ParkingSessionId,
            result.SourceChannel,
            result.EntitlementType,
            result.DecisionStatus,
            result.PolicyResolutionBasis,
            result.AppliedPolicyReferenceId,
            result.FallbackPolicyReferenceId,
            result.LocalOrdinanceApplied,
            result.GrossAmountMinorUnits,
            result.StatutoryDiscountAmountMinorUnits,
            result.NetPayableAmountMinorUnits,
            result.Currency,
            result.EvidenceRequired,
            result.EvidenceRecorded,
            result.ReasonCode,
            result.ErrorCode,
            result.CorrelationId,
            result.CreatedAt,
            result.DecidedAt,
            result.AppliedAt,
            result.OriginalTariffSnapshotId,
            result.AppliedTariffSnapshotId,
            result.DecisionCommandStatus,
            ResolveClientResultStatus(result),
            result.ResultClassification,
            result.SemanticHashSourceVersion,
            result.DecisionRetryable || result.ApplicationRetryable,
            ResolveRecoveryClassification(result),
            ResolveRecoveryAction(result),
            result.ErrorCode,
            result.DecisionCommandStatus,
            result.DecisionResultStatus,
            result.DecisionRetryable,
            result.DecisionRecoveryClassification,
            result.DecisionRecoveryAction,
            result.StatutoryDiscountPayableBasisApplicationCommandId,
            result.ApplicationRequested,
            result.ApplicationCommandStatus,
            result.ApplicationResultClassification,
            result.ApplicationSemanticHashSourceVersion,
            result.ApplicationRetryable,
            result.ApplicationRecoveryClassification,
            result.ApplicationRecoveryAction,
            result.OverallResultClassification,
            result.OneShotComplete,
            result.SiteId,
            result.SiteGroupId,
            result.VatExclusiveBasisAmountMinorUnits,
            result.VatAmountMinorUnits,
            result.VatTreatment,
            result.PayableBasisReady,
            result.PayableBasisReadinessStatus,
            result.PayableBasisReadinessAction);

    private static bool TryReadHeaders(
        HttpRequest request,
        out string? idempotencyKey,
        out Guid correlationId,
        out ErrorResponse? error)
    {
        idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            correlationId = Guid.Empty;
            error = BuildError("INVALID_REQUEST", "Idempotency-Key header is required.", Guid.Empty);
            return false;
        }

        return TryReadCorrelationId(request, out correlationId, out error);
    }

    private static bool TryReadCorrelationId(
        HttpRequest request,
        out Guid correlationId,
        out ErrorResponse? error)
    {
        var correlationRaw = request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (!Guid.TryParse(correlationRaw, out correlationId))
        {
            error = BuildError("INVALID_REQUEST", "X-Correlation-Id header is required.", Guid.Empty);
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsConflict(string errorCode) =>
        errorCode is "IDEMPOTENCY_SEMANTIC_CONFLICT"
            or "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT"
            or "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT"
            or "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS"
            or "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS";

    private static bool TryResolveAuthenticatedSourceChannel(
        HttpContext context,
        CentralPmsRbacOptions options,
        out string sourceChannel,
        out Guid actorId,
        out ErrorResponse? error)
    {
        var permissions = ReadPermissions(context, options);
        var channels = ChannelPermissions
            .Where(pair => permissions.Contains(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        actorId = ResolveActorId(context);
        if (actorId == Guid.Empty)
        {
            sourceChannel = string.Empty;
            error = new ErrorResponse
            {
                ErrorCode = "CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED",
                Message = "Authenticated user or service identity is required for statutory-discount decision submission."
            };
            return false;
        }

        if (channels.Length == 0)
        {
            sourceChannel = string.Empty;
            error = new ErrorResponse
            {
                ErrorCode = "CENTRAL_PMS_SOURCE_CHANNEL_FORBIDDEN",
                Message = "The caller is not authorized to submit statutory-discount decisions for a supported source channel.",
                Details = new Dictionary<string, object?>
                {
                    ["requiredPermissions"] = ChannelPermissions.Values.Order(StringComparer.Ordinal).ToArray()
                }
            };
            return false;
        }

        if (channels.Length > 1)
        {
            sourceChannel = string.Empty;
            error = new ErrorResponse
            {
                ErrorCode = "CENTRAL_PMS_SOURCE_CHANNEL_AMBIGUOUS",
                Message = "The authenticated identity maps to multiple statutory-discount source channels."
            };
            return false;
        }

        sourceChannel = channels[0];
        error = null;
        return true;
    }

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

    private static bool ValidateChannelFieldMatrix(
        StatutoryDiscountDecisionRequest body,
        string effectiveSourceChannel,
        Guid actorId,
        out ErrorResponse? error)
    {
        var operatorOnlyFieldsPresent =
            body.OperatorDeviceBindingId is not null ||
            body.OperatorShiftId is not null ||
            body.ReviewerUserId is not null ||
            body.ReviewerAttestation ||
            !string.IsNullOrWhiteSpace(body.Decision) ||
            !string.IsNullOrWhiteSpace(body.DecisionReasonCode);

        if (body.ActorUserId != Guid.Empty && body.ActorUserId != actorId)
        {
            error = new ErrorResponse
            {
                ErrorCode = "STATUTORY_DISCOUNT_ACTOR_CONTEXT_MISMATCH",
                Message = "Actor identity is server-derived and must match the authenticated identity when supplied."
            };
            return false;
        }

        if (effectiveSourceChannel is StatutoryDiscountSourceChannels.WebPay or StatutoryDiscountSourceChannels.AssistedPaymentTerminal &&
            operatorOnlyFieldsPresent)
        {
            error = new ErrorResponse
            {
                ErrorCode = "STATUTORY_DISCOUNT_CHANNEL_FIELD_PROHIBITED",
                Message = "Operator-only decision, reviewer, device, and shift fields are prohibited for this source channel.",
                Details = new Dictionary<string, object?>
                {
                    ["sourceChannel"] = effectiveSourceChannel
                }
            };
            return false;
        }

        error = null;
        return true;
    }

    private static Guid ResolveActorId(HttpContext context)
    {
        var userId = ResolveGuid(context.Request.Headers[CentralPmsRbacPolicyCatalog.UserIdHeaderName].FirstOrDefault()) ??
                     ResolveGuid(context.User.FindFirstValue(ClaimTypes.NameIdentifier)) ??
                     ResolveGuid(context.User.FindFirstValue("sub")) ??
                     ResolveGuid(context.User.FindFirstValue("user_id"));

        if (userId is not null && userId != Guid.Empty)
        {
            return userId.Value;
        }

        return ResolveGuid(context.Request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName].FirstOrDefault()) ??
               ResolveGuid(context.User.FindFirstValue("service_identity_id")) ??
               ResolveGuid(context.User.FindFirstValue("client_id")) ??
               Guid.Empty;
    }

    private static Guid? ResolveGuid(string? value) =>
        Guid.TryParse(value, out var guid) ? guid : null;

    private static readonly IReadOnlyDictionary<string, string> ChannelPermissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StatutoryDiscountSourceChannels.OperatorConsole] = "statutory-discounts.decision.submit.operator-console",
            [StatutoryDiscountSourceChannels.WebPay] = "statutory-discounts.decision.submit.webpay",
            [StatutoryDiscountSourceChannels.AssistedPaymentTerminal] = "statutory-discounts.decision.submit.assisted-payment-terminal"
        };

    private static string ResolveClientResultStatus(StatutoryDiscountDecisionResult result)
    {
        if (string.Equals(result.ResultClassification, "IDEMPOTENT_REPLAY", StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionClientResultStatuses.IdempotentReplay;
        }

        if (string.Equals(result.ResultClassification, "RECOVERABLE_USING_ORIGINAL_KEY", StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionClientResultStatuses.RecoverableUsingOriginalKey;
        }

        if (string.Equals(result.DecisionCommandStatus, StatutoryDiscountDecisionCommandStatuses.AwaitingReview, StringComparison.Ordinal) ||
            string.Equals(result.ResultClassification, StatutoryDiscountOneShotResultClassifications.AwaitingReview, StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionClientResultStatuses.AwaitingReview;
        }

        if (!result.OneShotComplete)
        {
            return StatutoryDiscountDecisionClientResultStatuses.InProgress;
        }

        return result.DecisionStatus switch
        {
            "APPLIED_PAYABLE_BASIS" or "APPROVED" => StatutoryDiscountDecisionClientResultStatuses.Approved,
            "REJECTED" => StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved,
            "PROCESSING" => StatutoryDiscountDecisionClientResultStatuses.InProgress,
            "AWAITING_REVIEW" => StatutoryDiscountDecisionClientResultStatuses.AwaitingReview,
            _ when !string.IsNullOrWhiteSpace(result.ErrorCode) => StatutoryDiscountDecisionClientResultStatuses.ValidationFailure,
            _ => StatutoryDiscountDecisionClientResultStatuses.CreatedDurablyCompleted
        };
    }

    private static string ResolveRecoveryClassification(StatutoryDiscountDecisionResult result)
    {
        if (result.ApplicationRetryable)
        {
            return result.ApplicationRecoveryClassification;
        }

        if (result.DecisionRetryable)
        {
            return result.DecisionRecoveryClassification;
        }

        if (string.Equals(result.DecisionRecoveryClassification, StatutoryDiscountDecisionRecoveryClassifications.AwaitingReview, StringComparison.Ordinal))
        {
            return result.DecisionRecoveryClassification;
        }

        if (string.Equals(result.ResultClassification, "IDEMPOTENT_REPLAY", StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult;
        }

        if (string.Equals(result.ResultClassification, "RECOVERABLE_USING_ORIGINAL_KEY", StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey;
        }

        return StatutoryDiscountDecisionRecoveryClassifications.None;
    }

    private static string? ResolveRecoveryAction(StatutoryDiscountDecisionResult result)
    {
        if (result.ApplicationRetryable)
        {
            return result.ApplicationRecoveryAction;
        }

        if (result.DecisionRetryable)
        {
            return result.DecisionRecoveryAction;
        }

        if (string.Equals(result.DecisionRecoveryClassification, StatutoryDiscountDecisionRecoveryClassifications.AwaitingReview, StringComparison.Ordinal))
        {
            return result.DecisionRecoveryAction;
        }

        if (string.Equals(result.ResultClassification, "IDEMPOTENT_REPLAY", StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionRecoveryActions.ReadCanonicalDecision;
        }

        if (string.Equals(result.ResultClassification, "RECOVERABLE_USING_ORIGINAL_KEY", StringComparison.Ordinal))
        {
            return StatutoryDiscountDecisionRecoveryActions.RetrySameRequestWithOriginalKey;
        }

        return null;
    }

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        Dictionary<string, object?>? details = null)
    {
        var retryable = errorCode is "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS"
            or "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE"
            or "STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE";
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable,
            ClientResultStatus = ResolveClientResultStatus(errorCode),
            RecoveryClassification = ResolveRecoveryClassification(errorCode),
            RecoveryAction = ResolveRecoveryAction(errorCode),
            Details = details
        };
    }

    private static string ResolveClientResultStatus(string errorCode) =>
        errorCode switch
        {
            "IDEMPOTENCY_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionClientResultStatuses.SemanticConflict,
            "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionClientResultStatuses.SemanticConflict,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionClientResultStatuses.SemanticConflict,
            "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS" => StatutoryDiscountDecisionClientResultStatuses.InProgress,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS" => StatutoryDiscountDecisionClientResultStatuses.InProgress,
            "STATUTORY_DISCOUNT_DECISION_NOT_FOUND" => StatutoryDiscountDecisionClientResultStatuses.NotFound,
            "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED" => StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved,
            "UNSAFE_IDENTIFIER_REJECTED" => StatutoryDiscountDecisionClientResultStatuses.UnsafeIdentityInput,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE" => StatutoryDiscountDecisionClientResultStatuses.TemporarilyUnavailable,
            "STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE" => StatutoryDiscountDecisionClientResultStatuses.TemporarilyUnavailable,
            "INVALID_REQUEST" or "UNSUPPORTED_SOURCE_CHANNEL" or "UNSUPPORTED_ENTITLEMENT_TYPE" =>
                StatutoryDiscountDecisionClientResultStatuses.ValidationFailure,
            _ => StatutoryDiscountDecisionClientResultStatuses.NonRetryableFailure
        };

    private static string ResolveRecoveryClassification(string errorCode) =>
        errorCode switch
        {
            "IDEMPOTENCY_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
            "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
            "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS" => StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS" => StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE" => StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
            "STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE" => StatutoryDiscountDecisionRecoveryClassifications.WaitThenRetryOriginalIdempotencyKey,
            "STATUTORY_DISCOUNT_DECISION_NOT_FOUND" => StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable,
            "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED" => StatutoryDiscountDecisionRecoveryClassifications.NotRecoverable,
            _ => StatutoryDiscountDecisionRecoveryClassifications.None
        };

    private static string? ResolveRecoveryAction(string errorCode) =>
        errorCode switch
        {
            "IDEMPOTENCY_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionRecoveryActions.SubmitCorrectedRequest,
            "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionRecoveryActions.SubmitCorrectedRequest,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT" => StatutoryDiscountDecisionRecoveryActions.SubmitCorrectedRequest,
            "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS" => StatutoryDiscountDecisionRecoveryActions.WaitAndRetry,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS" => StatutoryDiscountDecisionRecoveryActions.WaitAndRetry,
            "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE" => StatutoryDiscountDecisionRecoveryActions.WaitAndRetry,
            "STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE" => StatutoryDiscountDecisionRecoveryActions.WaitAndRetry,
            "STATUTORY_DISCOUNT_DECISION_NOT_FOUND" => StatutoryDiscountDecisionRecoveryActions.DoNotRetry,
            "STATUTORY_DISCOUNT_DECISION_NOT_APPROVED" => StatutoryDiscountDecisionRecoveryActions.DoNotRetry,
            _ => null
        };
}
