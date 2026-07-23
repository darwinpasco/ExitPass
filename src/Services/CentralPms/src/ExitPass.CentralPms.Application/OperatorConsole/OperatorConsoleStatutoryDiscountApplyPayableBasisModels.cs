namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command for applying an approved Operator Console statutory discount validation to payable basis.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountApplyPayableBasisCommand(
    Guid ValidationId,
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid? OriginalTariffSnapshotId,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Result for an access-gated statutory discount payable-basis application.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountApplyPayableBasisResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool ApplicationAccepted,
    bool ApplicationPersisted,
    Guid? PayableBasisApplicationId,
    Guid? StatutoryDiscountValidationId,
    Guid? ParkingSessionId,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    string? ApplicationStatus,
    bool AlreadyApplied,
    long? GrossAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? CurrencyCode,
    Guid? StatutoryDiscountPolicyId,
    Guid? ResolvedJurisdictionId,
    string? PolicyResolutionBasis,
    string? PolicyCode,
    string? BenefitType,
    string? NationalLawReference,
    string? OrdinanceReference,
    bool PolicySnapshotUsed,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId,
    Guid? StatutoryDiscountDecisionCommandId = null,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId = null);

/// <summary>
/// Persistence command for applying statutory discount payable basis.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand(
    Guid ValidationId,
    Guid? OriginalTariffSnapshotId,
    Guid AppliedByUserId,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Persistence result for statutory discount payable-basis application.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult(
    bool ApplicationAccepted,
    bool ApplicationPersisted,
    Guid? PayableBasisApplicationId,
    Guid? StatutoryDiscountValidationId,
    Guid? ParkingSessionId,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    string? ApplicationStatus,
    bool AlreadyApplied,
    long? GrossAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? CurrencyCode,
    Guid? StatutoryDiscountPolicyId,
    Guid? ResolvedJurisdictionId,
    string? PolicyResolutionBasis,
    string? PolicyCode,
    string? BenefitType,
    string? NationalLawReference,
    string? OrdinanceReference,
    bool PolicySnapshotUsed,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId = null);
