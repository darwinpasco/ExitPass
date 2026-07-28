namespace ExitPass.CentralPms.Contracts.TerminalCashPayments;

/// <summary>
/// APT request for resolving an authoritative payable basis before local cash acceptance.
/// </summary>
public sealed record AptPayableBasisResolveRequest(
    string SiteGroupId,
    string SiteId,
    string SitePosServerId,
    string TerminalId,
    string VendorSystemId,
    string ReferenceType,
    string? TicketReference,
    string? PlateNumber,
    Guid? StatutoryDiscountDecisionCommandId,
    Guid CorrelationId);

/// <summary>
/// APT request for immediately revalidating the displayed payable basis before CASH_RECEIVED.
/// </summary>
public sealed record AptPayableBasisRevalidateRequest(
    string ParkingSessionId,
    string TariffSnapshotId,
    string SiteGroupId,
    string SiteId,
    string SitePosServerId,
    string TerminalId,
    string VendorSystemId,
    string? TicketReference,
    string? PlateNumber,
    long ExpectedAmountMinorUnits,
    string ExpectedCurrency,
    Guid? StatutoryDiscountDecisionCommandId,
    Guid CorrelationId);

/// <summary>
/// APT-safe authoritative payable-basis response.
/// </summary>
public sealed record AptPayableBasisReadinessResponse(
    string Operation,
    string? RevalidationOutcome,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid SiteGroupId,
    Guid SiteId,
    Guid SitePosServerId,
    string TerminalId,
    string? SiteGroupName,
    string? SiteName,
    string? TicketReference,
    string? PlateNumber,
    DateTimeOffset? EntryTimestamp,
    string ParkingStatus,
    string PaymentStatus,
    long AuthoritativeAmountMinorUnits,
    string Currency,
    DateTimeOffset TariffCalculatedAt,
    DateTimeOffset TariffValidUntil,
    DateTimeOffset FeeValidUntil,
    string VendorSystemId,
    IReadOnlyList<AptReadinessDimensionDto> ReadinessDimensions,
    string SessionReadiness,
    string TariffReadiness,
    string PaymentEligibility,
    string TerminalCashAvailability,
    string FiscalReadiness,
    string SalesInvoiceConfigurationReadiness,
    AptStatutoryDiscountReadinessDto? StatutoryDiscountReadiness,
    string CashAcceptanceReadiness,
    bool ReadyForCashAcceptance,
    IReadOnlyList<string> BlockingReasonCodes,
    bool Retryable,
    string SafeUserFacingClassification,
    Guid CorrelationId);

/// <summary>
/// One readiness dimension returned for APT display and support diagnostics.
/// </summary>
public sealed record AptReadinessDimensionDto(
    string Name,
    string Status,
    bool Ready,
    string? BlockingReasonCode,
    bool Retryable,
    string Message);

/// <summary>
/// APT-safe statutory-discount readiness dimension derived from canonical Central PMS readback.
/// </summary>
public sealed record AptStatutoryDiscountReadinessDto(
    bool Applicable,
    bool Ready,
    Guid? StatutoryDiscountDecisionCommandId,
    Guid? StatutoryDiscountValidationId,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId,
    string? EntitlementType,
    string? DecisionStatus,
    string? DecisionResultStatus,
    string? DecisionCommandStatus,
    string? ApplicationCommandStatus,
    string? ApplicationResultClassification,
    bool PayableBasisReady,
    string PayableBasisReadinessStatus,
    string? PayableBasisReadinessAction,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    long? OriginalAmountMinorUnits,
    long? VatExclusiveBasisAmountMinorUnits,
    long? VatAmountMinorUnits,
    string? VatTreatment,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? Currency,
    bool Retryable,
    string? RecoveryClassification,
    string? RecoveryAction,
    string? SafeErrorCode,
    string? BlockingReasonCode,
    string Message);
