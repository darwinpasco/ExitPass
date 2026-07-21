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
        activity?.SetTag("source_channel", body?.SourceChannel);
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

        if (!StatutoryDiscountSourceChannels.IsSupported(body.SourceChannel))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unsupported source channel.");
            return Results.BadRequest(BuildError(
                "UNSUPPORTED_SOURCE_CHANNEL",
                "Source channel must be OPERATOR_CONSOLE, WEBPAY, or ASSISTED_PAYMENT_TERMINAL.",
                correlationId));
        }

        if (!HasSourceChannelPermission(request.HttpContext, rbacOptions.Value, body.SourceChannel, out var requiredPermission))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Source channel permission is missing.");
            return Results.Json(
                BuildError(
                    "CENTRAL_PMS_SOURCE_CHANNEL_FORBIDDEN",
                    $"The caller is not authorized to submit statutory-discount decisions for source channel {body.SourceChannel}.",
                    correlationId,
                    new Dictionary<string, object?> { ["requiredPermission"] = requiredPermission }),
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await service.SubmitAsync(ToCommand(body, idempotencyKey!, correlationId), cancellationToken)
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
        string idempotencyKey,
        Guid correlationId) =>
        new(
            body.RequestReference,
            body.SourceChannel,
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
            body.ActorUserId,
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
            result.ResultClassification,
            result.SemanticHashSourceVersion);

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
        errorCode is "IDEMPOTENCY_SEMANTIC_CONFLICT" or "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS";

    private static bool HasSourceChannelPermission(
        HttpContext context,
        CentralPmsRbacOptions options,
        string? sourceChannel,
        out string requiredPermission)
    {
        requiredPermission = RequiredSourceChannelPermission(sourceChannel);
        if (string.IsNullOrWhiteSpace(requiredPermission))
        {
            return false;
        }

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

        return permissions.Contains(requiredPermission) || permissions.Contains("reconciliation.manage");
    }

    private static string RequiredSourceChannelPermission(string? sourceChannel) =>
        string.IsNullOrWhiteSpace(sourceChannel)
            ? string.Empty
            : sourceChannel.Trim().ToUpperInvariant() switch
        {
            "OPERATOR_CONSOLE" => "statutory-discounts.decision.submit.operator-console",
            "WEBPAY" => "statutory-discounts.decision.submit.webpay",
            "ASSISTED_PAYMENT_TERMINAL" => "statutory-discounts.decision.submit.assisted-payment-terminal",
            _ => string.Empty
        };

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        Dictionary<string, object?>? details = null) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false,
            Details = details
        };
}
