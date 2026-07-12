using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console statutory discount policy resolution endpoint.
///
/// ExitPass v1.2 Invariants Enforced:
/// - This endpoint persists Operator Console access evaluation before returning policy details.
/// - This endpoint resolves verified local policy or RA 9994 / RA 10754 national fallback.
/// - This endpoint does not create drafts, approve/reject discounts, apply discounts, mutate payable basis,
///   create payment attempts, call providers, open gates, create coupons, or create reconciliation records.
/// </summary>
public static class OperatorConsoleStatutoryDiscountPolicyResolutionEndpoints
{
    private const string PolicyResolvePolicy = "OperatorConsoleStatutoryDiscountPolicyResolve";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountPolicyResolution");

    /// <summary>
    /// Maps Operator Console statutory discount policy resolution endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleStatutoryDiscountPolicyResolutionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapPost("/statutory-discounts/resolve-policy", ResolvePolicyAsync)
            .WithName("ResolveOperatorConsoleStatutoryDiscountPolicy")
            .WithTags("OperatorConsole")
            .Accepts<OperatorConsoleStatutoryDiscountPolicyResolutionRequest>("application/json")
            .Produces<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleStatutoryDiscountPolicyResolutionResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ReconciliationPolicyMetadata(PolicyResolvePolicy))
            .WithSummary("Resolve Operator Console statutory discount policy")
            .WithDescription("Resolves the verified local statutory discount policy for the site jurisdiction or mandatory RA 9994 / RA 10754 national fallback after evaluating and persisting Operator Console access. This endpoint is read-only except for access evaluation persistence and does not create drafts, apply discounts, mutate payable basis, or create payment, gate, provider, coupon, or reconciliation records.");

        return app;
    }

    private static async Task<IResult> ResolvePolicyAsync(
        OperatorConsoleStatutoryDiscountPolicyResolutionRequest request,
        HttpRequest httpRequest,
        IOperatorConsoleStatutoryDiscountPolicyResolutionService service,
        ILoggerFactory loggerFactory)
    {
        using var activity = ActivitySource.StartActivity("HTTP ResolveOperatorConsoleStatutoryDiscountPolicy", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountPolicyResolutionEndpoints");

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", request.CorrelationId);
        activity?.SetTag("site_id", request.SiteId);
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

            var result = await service.ResolveAsync(
                new OperatorConsoleStatutoryDiscountPolicyResolutionCommand(
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId ?? request.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    request.ParkingSessionId,
                    request.EntitlementType,
                    request.IdempotencyKey,
                    identity.CorrelationId),
                httpRequest.HttpContext.RequestAborted);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("policy_resolved", result.PolicyResolved);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Operator Console statutory discount policy resolution completed. evaluation_id={EvaluationId} access_allowed={AccessAllowed} policy_resolved={PolicyResolved}",
                result.AccessEvaluationId,
                result.AccessAllowed,
                result.PolicyResolved);

            var response = ToContract(result);
            return result.AccessAllowed && result.ErrorCode is "SITE_NOT_FOUND" or "NATIONAL_FALLBACK_POLICY_NOT_CONFIGURED"
                ? Results.NotFound(response)
                : Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError("INVALID_OPERATOR_CONSOLE_POLICY_RESOLUTION_REQUEST", ex.Message, request.CorrelationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console statutory discount policy resolution failed.");
            return Results.Json(
                BuildError(
                    "POLICY_RESOLUTION_FAILED",
                    "The Operator Console statutory discount policy resolution could not be completed.",
                    request.CorrelationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OperatorConsoleStatutoryDiscountPolicyResolutionResponse ToContract(
        OperatorConsoleStatutoryDiscountPolicyResolutionResult result)
    {
        var policy = result.Policy;
        return new OperatorConsoleStatutoryDiscountPolicyResolutionResponse(
            result.AccessEvaluationId,
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            result.PolicyResolved,
            policy?.StatutoryDiscountPolicyId,
            policy?.JurisdictionId,
            policy?.SiteId,
            policy?.SiteGroupId,
            policy?.EntitlementType,
            policy?.PolicyCode,
            policy?.PolicyName,
            policy?.PolicyResolutionBasis,
            policy?.PolicyLevel,
            policy?.PolicyType,
            policy?.LegalBasisReference,
            policy?.OrdinanceReference,
            policy?.NationalLawReference,
            policy?.VerificationStatus,
            policy?.BeneficiaryResidencyScope,
            policy?.BenefitType,
            policy?.FreeDurationMinutes,
            policy?.InitialRateExempt,
            policy?.FullFeeExempt,
            policy?.OvernightExcluded,
            policy?.ValetExcluded,
            policy?.StandaloneParkingExcluded,
            policy?.DriverOrPassengerRequired,
            policy?.FreePeriodApplication,
            policy?.SucceedingHoursDiscountRule,
            policy?.DiscountBaseScope,
            policy?.StackingPolicy,
            policy?.LegalBasisPriority,
            policy?.RequiresOperatorValidation,
            policy?.RequiresEvidence,
            policy?.EffectiveFrom,
            policy?.EffectiveTo,
            policy?.SourceReference,
            policy?.PolicySnapshot,
            result.IneligibilityReason,
            result.ErrorCode,
            result.CorrelationId,
            result.PolicyReadinessClassification,
            result.RequiresManualReview,
            result.PolicyReadinessReason,
            result.OperatorMessage);
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };
}
