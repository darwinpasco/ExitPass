namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for applying an approved Operator Console statutory discount validation to payable basis.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountApplyPayableBasisRequest(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid? OriginalTariffSnapshotId,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Response body for an access-gated Operator Console statutory discount payable-basis application.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountApplyPayableBasisResponse(
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
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId);
