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
            .WithTags("StatutoryDiscounts")
            .AcceptAuthenticatedServicePrincipal();

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

        group.MapPost("/availability", ResolveAvailabilityAsync)
            .WithName("ResolveStatutoryParkingDiscountAvailability")
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<StatutoryDiscountParkingAvailabilityResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> ResolveAvailabilityAsync(
        HttpRequest request,
        StatutoryDiscountParkingAvailabilityRequestDto? body,
        IStatutoryDiscountDecisionFacadeService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ResolveStatutoryParkingDiscountAvailability", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/statutory-discounts/decisions/availability");
        activity?.SetTag("parking_session_id", body?.ParkingSessionId);
        activity?.SetTag("requested_entitlement_type", body?.RequestedEntitlementType);

        if (body is null)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "Request body is required.", Guid.Empty));
        }

        if (!TryReadCorrelationId(request, out var correlationId, out var headerError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, headerError!.Message);
            return Results.BadRequest(headerError);
        }

        try
        {
            var result = await service.ResolveAvailabilityAsync(
                    new StatutoryDiscountParkingAvailabilityRequest(
                        body.RequestReference,
                        body.ParkingSessionId,
                        body.RequestedEntitlementType,
                        body.BeneficiaryResidencySatisfied,
                        correlationId),
                    cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("availability_status", result.AvailabilityStatus);
            return Results.Ok(ToAvailabilityResponse(result));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.BadRequest(BuildError("INVALID_REQUEST", ex.Message, correlationId));
        }
    }

    private static async Task<IResult> SubmitAsync(
        HttpRequest request,
        StatutoryDiscountDecisionRequest? body,
        IStatutoryDiscountDecisionFacadeService service,
        IOptions<CentralPmsRbacOptions> rbacOptions,
        IWebHostEnvironment environment,
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

        if (string.Equals(
                requestedSourceChannel,
                StatutoryDiscountSourceChannels.AssistedPaymentTerminal,
                StringComparison.Ordinal))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "APT cannot originate statutory-discount requests.");
            return Results.BadRequest(BuildError(
                "STATUTORY_DISCOUNT_APT_REQUEST_ORIGIN_PROHIBITED",
                "Statutory-discount requests may originate only from WebPay or Operator Console.",
                correlationId));
        }

        if (!TryResolveAuthenticatedSourceChannel(
                request.HttpContext,
                rbacOptions.Value,
                environment,
                out var effectiveSourceChannel,
                out var actorId,
                out var serviceIdentityId,
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

        var serviceIdentity = FindAuthenticatedIdentity(
            request.HttpContext.User,
            "InternalMtlsServicePrincipal");
        if ((effectiveSourceChannel is StatutoryDiscountSourceChannels.WebPay or StatutoryDiscountSourceChannels.AssistedPaymentTerminal) &&
            serviceIdentity is not null &&
            !IdentityHasRequestedScope(serviceIdentity, body.SiteId, body.SiteGroupId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authenticated service principal is outside the requested Site scope.");
            return Results.Json(
                BuildError(
                    "CENTRAL_PMS_SERVICE_PRINCIPAL_SCOPE_DENIED",
                    "The authenticated service principal is not authorized for the requested scope.",
                    correlationId),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var humanIdentity = FindAuthenticatedIdentity(
            request.HttpContext.User,
            HumanSessionAuthenticationHandler.SchemeName);
        if (effectiveSourceChannel is StatutoryDiscountSourceChannels.AssistedPaymentTerminal &&
            humanIdentity is not null &&
            !IdentityHasRequestedScope(humanIdentity, body.SiteId, body.SiteGroupId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Authenticated APT cashier is outside the requested Site scope.");
            return Results.Json(
                BuildError(
                    "CENTRAL_PMS_APT_HUMAN_SCOPE_DENIED",
                    "The authenticated APT cashier is not authorized for the requested scope.",
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
                    ToCommand(body, effectiveSourceChannel, actorId, serviceIdentityId, idempotencyKey!, correlationId),
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
        Guid? serviceIdentityId,
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
            body.BeneficiaryResidencySatisfied,
            idempotencyKey,
            correlationId,
            serviceIdentityId.HasValue
                ? new StatutoryDiscountServiceChannelCallerContext(
                    serviceIdentityId.Value,
                    effectiveSourceChannel,
                    effectiveSourceChannel,
                    ChannelPermissions[effectiveSourceChannel])
                : null)
        {
            IdControlReference = body.IdControlReference
        };

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
            result.PayableBasisReadinessAction,
            result.ZeroPayableStatutoryFinality is null
                ? null
                : new StatutoryDiscountZeroPayableFinalityResponse(
                    result.ZeroPayableStatutoryFinality.ParkingSessionId,
                    result.ZeroPayableStatutoryFinality.StatutoryDiscountDecisionCommandId,
                    result.ZeroPayableStatutoryFinality.StatutoryDiscountPayableBasisApplicationCommandId,
                    result.ZeroPayableStatutoryFinality.StatutoryDiscountValidationId,
                    result.ZeroPayableStatutoryFinality.AppliedPolicyReferenceId,
                    result.ZeroPayableStatutoryFinality.OriginalTariffSnapshotId,
                    result.ZeroPayableStatutoryFinality.AppliedTariffSnapshotId,
                    result.ZeroPayableStatutoryFinality.SiteId,
                    result.ZeroPayableStatutoryFinality.SiteGroupId,
                    result.ZeroPayableStatutoryFinality.EntitlementType,
                    result.ZeroPayableStatutoryFinality.BenefitType,
                    result.ZeroPayableStatutoryFinality.OriginalAmountMinorUnits,
                    result.ZeroPayableStatutoryFinality.StatutoryWaiverAmountMinorUnits,
                    result.ZeroPayableStatutoryFinality.VatAmountMinorUnits,
                    result.ZeroPayableStatutoryFinality.FinalPayableAmountMinorUnits,
                    result.ZeroPayableStatutoryFinality.Currency,
                    result.ZeroPayableStatutoryFinality.SourceChannel,
                    result.ZeroPayableStatutoryFinality.DecidedAt,
                    result.ZeroPayableStatutoryFinality.AppliedAt,
                    result.ZeroPayableStatutoryFinality.CorrelationId,
                    result.ZeroPayableStatutoryFinality.FinalityState),
            result.CompletionAuthority is null
                ? null
                : new CompletionAuthorityResponse(
                    result.CompletionAuthority.ParkingSessionId,
                    result.CompletionAuthority.TariffSnapshotId,
                    result.CompletionAuthority.SiteId,
                    result.CompletionAuthority.SiteGroupId,
                    result.CompletionAuthority.CompletionBasis,
                    result.CompletionAuthority.DurableSourceReferenceId,
                    result.CompletionAuthority.EstablishedAt,
                    result.CompletionAuthority.CorrelationId,
                    result.CompletionAuthority.FinalPayableAmountMinorUnits,
                    result.CompletionAuthority.Currency,
                    result.CompletionAuthority.PaymentAttemptId,
                    result.CompletionAuthority.PaymentConfirmationId,
                    result.CompletionAuthority.StatutoryDiscountDecisionCommandId,
                    result.CompletionAuthority.StatutoryDiscountPayableBasisApplicationCommandId,
                    result.CompletionAuthority.StatutoryDiscountValidationId,
                    result.CompletionAuthority.AppliedPolicyReferenceId,
                    result.CompletionAuthority.StatutoryDiscountPolicyVersionId,
                    result.CompletionAuthority.AuthorityState),
            result.ExitAuthorizationEligibility is null
                ? null
                : new ExitAuthorizationEligibilityResponse(
                    result.ExitAuthorizationEligibility.CompletionAuthorityEligible,
                    result.ExitAuthorizationEligibility.ExitAuthorizationIssuanceAllowed,
                    result.ExitAuthorizationEligibility.Status,
                    result.ExitAuthorizationEligibility.BlockedReason,
                    result.ExitAuthorizationEligibility.CompletionBasis),
            result.ZeroPayableFiscalCompletion is null
                ? null
                : new ZeroPayableStatutoryFiscalCompletionResponse(
                    result.ZeroPayableFiscalCompletion.FiscalIssuanceReferenceId,
                    result.ZeroPayableFiscalCompletion.FiscalPrerequisiteSatisfied,
                    result.ZeroPayableFiscalCompletion.PosServerCallAttempted,
                    result.ZeroPayableFiscalCompletion.FiscalIssuanceState.ToString(),
                    result.ZeroPayableFiscalCompletion.PosServerFiscalDocumentId,
                    result.ZeroPayableFiscalCompletion.FiscalDocumentNumber,
                    result.ZeroPayableFiscalCompletion.ElectronicJournalEventReference,
                    result.ZeroPayableFiscalCompletion.CompletionBasis,
                    result.ZeroPayableFiscalCompletion.CompletionAuthorityReferenceId,
                    result.ZeroPayableFiscalCompletion.SafeErrorCode),
            result.ExitAuthorization is null
                ? null
                : new StatutoryExitAuthorizationResponse(
                    result.ExitAuthorization.ExitAuthorizationId,
                    result.ExitAuthorization.ParkingSessionId,
                    result.ExitAuthorization.TariffSnapshotId,
                    result.ExitAuthorization.CompletionBasis,
                    result.ExitAuthorization.CompletionAuthorityReferenceId,
                    result.ExitAuthorization.PaymentAttemptId,
                    result.ExitAuthorization.PaymentConfirmationId,
                    result.ExitAuthorization.AuthorizationStatus,
                    result.ExitAuthorization.IssuedAt,
                    result.ExitAuthorization.ExpirationTimestamp));

    private static StatutoryDiscountParkingAvailabilityResponse ToAvailabilityResponse(
        StatutoryDiscountParkingAvailabilityResult result) =>
        new(
            result.RequestReference,
            result.ParkingSessionId,
            result.SiteId,
            result.SiteGroupId,
            result.JurisdictionId,
            result.JurisdictionCode,
            result.JurisdictionDisplayName,
            result.AvailabilityStatus,
            result.StatutoryParkingBenefitAvailable,
            result.CoveredEntitlementTypes,
            result.RequestedEntitlementType,
            result.PolicyVersionId,
            result.PolicyCode,
            result.PolicyVersion,
            result.OrdinanceNumber,
            result.OrdinanceTitle,
            result.PolicyDisplayName,
            result.VerificationStatus,
            result.PublicationStatus,
            result.EffectiveFrom,
            result.EffectiveTo,
            result.ResidencyRequirement,
            result.RequiredEvidenceTypes.Select(requirement => new StatutoryDiscountPolicyEvidenceRequirementDto(
                    requirement.EvidenceType,
                    requirement.RequirementStatus,
                    requirement.SafeRequirementLabel,
                    requirement.SafeRequirementNotes))
                .ToArray(),
            result.ParkingServiceApplicability,
            result.BenefitEffectClassification,
            result.BenefitEffectSupportStatus,
            result.OfficialSourceAvailable,
            result.OrdinanceTextAvailable,
            result.OrdinanceNumberAvailable,
            result.SafeReasonCode,
            result.Retryable,
            result.RemediationAction,
            result.CorrelationId);

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
        IWebHostEnvironment environment,
        out string sourceChannel,
        out Guid actorId,
        out Guid? serviceIdentityId,
        out ErrorResponse? error)
    {
        var fixtureAuthorityAllowed =
            (environment.IsDevelopment() || environment.IsEnvironment("SecureDevelopment") || environment.IsEnvironment("Test")) &&
            options.AllowFixtureIdentityHeaders;
        var servicePrincipal = FindAuthenticatedIdentity(context.User, "InternalMtlsServicePrincipal");
        var humanPrincipal = FindAuthenticatedIdentity(context.User, HumanSessionAuthenticationHandler.SchemeName);
        var permissions = fixtureAuthorityAllowed
            ? ReadPermissions(context, options, fixtureAuthorityAllowed)
            : ReadPermissions(servicePrincipal ?? humanPrincipal);
        var channels = ChannelPermissions
            .Where(pair => permissions.Contains(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        var userId = ResolveUserId(context, humanPrincipal, fixtureAuthorityAllowed);
        serviceIdentityId = ResolveServiceIdentityId(context, servicePrincipal, fixtureAuthorityAllowed);
        actorId = Guid.Empty;

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

        var resolvedChannel = channels[0];
        var serviceChannel = resolvedChannel is StatutoryDiscountSourceChannels.WebPay or
            StatutoryDiscountSourceChannels.AssistedPaymentTerminal;
        if (resolvedChannel == StatutoryDiscountSourceChannels.WebPay)
        {
            if (!serviceIdentityId.HasValue || userId.HasValue)
            {
                return ActorTypeMismatch(out sourceChannel, out error);
            }

            actorId = serviceIdentityId.Value;
        }
        else if (resolvedChannel == StatutoryDiscountSourceChannels.AssistedPaymentTerminal)
        {
            if (!serviceIdentityId.HasValue || !userId.HasValue)
            {
                sourceChannel = string.Empty;
                error = new ErrorResponse
                {
                    ErrorCode = !userId.HasValue
                        ? "CENTRAL_PMS_APT_HUMAN_SESSION_REQUIRED"
                        : "CENTRAL_PMS_AUTHENTICATED_ACTOR_TYPE_MISMATCH",
                    Message = !userId.HasValue
                        ? "An authenticated APT cashier session is required for statutory-discount submission."
                        : "The authenticated actor type is not valid for the requested statutory-discount channel."
                };
                return false;
            }

            if (!fixtureAuthorityAllowed)
            {
                var humanAudience = humanPrincipal?.FindFirst("exitpass_audience")?.Value;
                var boundDeviceServiceIdentityId = ResolveGuid(
                    humanPrincipal?.FindFirst("device_service_identity_id")?.Value);
                if (!string.Equals(humanAudience, "APT", StringComparison.Ordinal))
                {
                    sourceChannel = string.Empty;
                    error = new ErrorResponse
                    {
                        ErrorCode = "CENTRAL_PMS_APT_HUMAN_AUDIENCE_DENIED",
                        Message = "The authenticated human session is not valid for the APT audience."
                    };
                    return false;
                }

                if (!boundDeviceServiceIdentityId.HasValue ||
                    boundDeviceServiceIdentityId != serviceIdentityId)
                {
                    sourceChannel = string.Empty;
                    error = new ErrorResponse
                    {
                        ErrorCode = "CENTRAL_PMS_APT_HUMAN_DEVICE_BINDING_MISMATCH",
                        Message = "The authenticated APT cashier session is not bound to the authenticated terminal identity."
                    };
                    return false;
                }
            }

            actorId = userId.Value;
        }
        else if (!userId.HasValue || serviceIdentityId.HasValue)
        {
            return ActorTypeMismatch(out sourceChannel, out error);
        }
        else
        {
            actorId = userId.Value;
        }

        if (serviceChannel && !fixtureAuthorityAllowed)
        {
            var authenticatedAudience = servicePrincipal?.FindFirst("exitpass_audience")?.Value;
            var authenticatedSourceChannel = StatutoryDiscountSourceChannels.Normalize(
                servicePrincipal?.FindFirst("source_channel")?.Value ?? string.Empty);
            if (servicePrincipal?.IsAuthenticated != true ||
                !string.Equals(authenticatedAudience, "CENTRAL_PMS", StringComparison.Ordinal) ||
                !string.Equals(authenticatedSourceChannel, resolvedChannel, StringComparison.Ordinal))
            {
                sourceChannel = string.Empty;
                error = new ErrorResponse
                {
                    ErrorCode = "CENTRAL_PMS_SERVICE_PRINCIPAL_ADMISSION_DENIED",
                    Message = "An authenticated service principal with the required audience and source channel is required."
                };
                return false;
            }
        }

        sourceChannel = resolvedChannel;
        error = null;
        return true;
    }

    private static bool ActorTypeMismatch(out string sourceChannel, out ErrorResponse? error)
    {
        sourceChannel = string.Empty;
        error = new ErrorResponse
        {
            ErrorCode = "CENTRAL_PMS_AUTHENTICATED_ACTOR_TYPE_MISMATCH",
            Message = "The authenticated actor type is not valid for the requested statutory-discount channel."
        };
        return false;
    }

    private static IReadOnlySet<string> ReadPermissions(
        HttpContext context,
        CentralPmsRbacOptions options,
        bool fixtureAuthorityAllowed)
    {
        var permissions = context.User.Claims
            .Where(claim => string.Equals(claim.Type, CentralPmsRbacPolicyCatalog.PermissionClaimType, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "scope", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (fixtureAuthorityAllowed && options.AllowPermissionHeader &&
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

    private static IReadOnlySet<string> ReadPermissions(ClaimsIdentity? identity) =>
        identity?.Claims
            .Where(claim => string.Equals(claim.Type, CentralPmsRbacPolicyCatalog.PermissionClaimType, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(claim.Type, "scope", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        if (effectiveSourceChannel is StatutoryDiscountSourceChannels.WebPay or StatutoryDiscountSourceChannels.AssistedPaymentTerminal &&
            body.ActorUserId != Guid.Empty)
        {
            error = new ErrorResponse
            {
                ErrorCode = "STATUTORY_DISCOUNT_ACTOR_FIELD_PROHIBITED",
                Message = "Service-channel actor identity is derived from authenticated server context and cannot be supplied in the request body."
            };
            return false;
        }

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

    private static Guid? ResolveUserId(
        HttpContext context,
        ClaimsIdentity? humanIdentity,
        bool fixtureAuthorityAllowed) =>
        (fixtureAuthorityAllowed
            ? ResolveGuid(context.Request.Headers[CentralPmsRbacPolicyCatalog.UserIdHeaderName].FirstOrDefault())
            : null) ?? ResolveGuid(humanIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value) ??
        ResolveGuid(humanIdentity?.FindFirst("sub")?.Value) ??
        ResolveGuid(humanIdentity?.FindFirst("user_id")?.Value);

    private static Guid? ResolveServiceIdentityId(
        HttpContext context,
        ClaimsIdentity? serviceIdentity,
        bool fixtureAuthorityAllowed) =>
        (fixtureAuthorityAllowed
            ? ResolveGuid(context.Request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName].FirstOrDefault())
            : null) ?? ResolveGuid(serviceIdentity?.FindFirst("service_identity_id")?.Value) ??
        ResolveGuid(serviceIdentity?.FindFirst("client_id")?.Value);

    private static Guid? ResolveGuid(string? value) =>
        Guid.TryParse(value, out var guid) ? guid : null;

    private static ClaimsIdentity? FindAuthenticatedIdentity(ClaimsPrincipal principal, string authenticationType) =>
        principal.Identities.SingleOrDefault(identity =>
            identity.IsAuthenticated &&
            string.Equals(identity.AuthenticationType, authenticationType, StringComparison.Ordinal));

    private static bool IdentityHasRequestedScope(
        ClaimsIdentity identity,
        Guid? siteId,
        Guid? siteGroupId)
    {
        if (!siteId.HasValue || siteId == Guid.Empty)
        {
            return false;
        }

        var siteAllowed = identity.FindAll("site_id")
            .Select(claim => ResolveGuid(claim.Value))
            .Any(candidate => candidate == siteId);
        if (!siteAllowed)
        {
            return false;
        }

        return !siteGroupId.HasValue || siteGroupId == Guid.Empty || identity.FindAll("site_group_id")
            .Select(claim => ResolveGuid(claim.Value))
            .Any(candidate => candidate == siteGroupId);
    }

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
