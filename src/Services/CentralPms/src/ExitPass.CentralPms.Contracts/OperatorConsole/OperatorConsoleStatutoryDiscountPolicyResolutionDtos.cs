using System.Text.Json;

namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for resolving the statutory discount policy applicable to an Operator Console workflow.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountPolicyResolutionRequest(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid? ParkingSessionId,
    string EntitlementType,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Response body for access-gated read-only statutory discount policy resolution.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountPolicyResolutionResponse(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool PolicyResolved,
    Guid? StatutoryDiscountPolicyId,
    Guid? JurisdictionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? EntitlementType,
    string? PolicyCode,
    string? PolicyName,
    string? PolicyResolutionBasis,
    string? PolicyLevel,
    string? PolicyType,
    string? LegalBasisReference,
    string? OrdinanceReference,
    string? NationalLawReference,
    string? VerificationStatus,
    string? BeneficiaryResidencyScope,
    string? BenefitType,
    int? FreeDurationMinutes,
    bool? InitialRateExempt,
    bool? FullFeeExempt,
    bool? OvernightExcluded,
    bool? ValetExcluded,
    bool? StandaloneParkingExcluded,
    bool? DriverOrPassengerRequired,
    string? FreePeriodApplication,
    string? SucceedingHoursDiscountRule,
    string? DiscountBaseScope,
    string? StackingPolicy,
    string? LegalBasisPriority,
    bool? RequiresOperatorValidation,
    bool? RequiresEvidence,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? SourceReference,
    JsonElement? PolicySnapshot,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId);
