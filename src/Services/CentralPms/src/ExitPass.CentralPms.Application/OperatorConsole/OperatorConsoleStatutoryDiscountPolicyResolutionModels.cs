using System.Text.Json;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command for resolving the statutory discount policy applicable to an Operator Console workflow.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountPolicyResolutionCommand(
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
/// Result for an access-gated read-only statutory discount policy resolution.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountPolicyResolutionResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool PolicyResolved,
    OperatorConsoleResolvedStatutoryDiscountPolicy? Policy,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId);

/// <summary>
/// Read request for statutory discount policy resolution.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountPolicyResolutionReadRequest(
    Guid SiteId,
    Guid? SiteGroupId,
    string EntitlementType,
    DateOnly EffectiveDate);

/// <summary>
/// Read result for statutory discount policy resolution.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountPolicyResolutionReadResult(
    bool Resolved,
    OperatorConsoleResolvedStatutoryDiscountPolicy? Policy,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? JurisdictionId,
    string? IneligibilityReason,
    string? ErrorCode);

/// <summary>
/// Resolved statutory discount policy read model.
/// </summary>
public sealed record OperatorConsoleResolvedStatutoryDiscountPolicy(
    Guid StatutoryDiscountPolicyId,
    Guid? JurisdictionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string PolicyCode,
    string PolicyName,
    string PolicyResolutionBasis,
    string PolicyLevel,
    string PolicyType,
    string? LegalBasisReference,
    string? OrdinanceReference,
    string? NationalLawReference,
    string VerificationStatus,
    string BeneficiaryResidencyScope,
    string BenefitType,
    int? FreeDurationMinutes,
    bool InitialRateExempt,
    bool FullFeeExempt,
    bool OvernightExcluded,
    bool ValetExcluded,
    bool StandaloneParkingExcluded,
    bool DriverOrPassengerRequired,
    string FreePeriodApplication,
    string SucceedingHoursDiscountRule,
    string DiscountBaseScope,
    string StackingPolicy,
    string LegalBasisPriority,
    bool RequiresOperatorValidation,
    bool RequiresEvidence,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? SourceReference,
    JsonElement PolicySnapshot);
