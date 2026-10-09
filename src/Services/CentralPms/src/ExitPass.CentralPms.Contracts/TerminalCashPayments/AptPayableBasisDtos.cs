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
    Guid? ParkingSessionId,
    Guid? TariffSnapshotId,
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
    string? PaymentStatus,
    long? AuthoritativeAmountMinorUnits,
    bool? CustomerInformationSubmitted,
    string? Currency,
    DateTimeOffset? TariffCalculatedAt,
    DateTimeOffset? TariffValidUntil,
    DateTimeOffset? FeeValidUntil,
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
    Guid CorrelationId)
{
    /// <summary>Whether a safe session identity was found.</summary>
    public bool SessionFound { get; init; } = true;

    /// <summary>Live vendor or continuity-projection source.</summary>
    public string SessionSource { get; init; } = "LIVE_VENDOR";

    /// <summary>Whether the response is a degraded session-only result.</summary>
    public bool Degraded { get; init; }

    /// <summary>Whether authoritative payable-basis facts accompany the session.</summary>
    public bool PayableBasisAvailable { get; init; } = true;

    /// <summary>Live vendor or ExitPass continuity tariff source.</summary>
    public string? TariffSource { get; init; }

    /// <summary>Whether completion requires operator-assisted manual exit.</summary>
    public bool ManualExitRequired { get; init; }

    /// <summary>Canonical vehicle classification used by Central PMS.</summary>
    public string? VehicleTypeCode { get; init; }

    /// <summary>Immutable tariff version reference used by the payable basis.</summary>
    public string? TariffVersion { get; init; }

    /// <summary>Canonical zero-payable fiscal and exit completion readback, when applicable.</summary>
    public AptZeroPayableStatutoryCompletionDto? ZeroPayableStatutoryCompletion { get; init; }

    /// <summary>Projection identifier retained for bounded support traceability.</summary>
    public Guid? VendorSessionProjectionId { get; init; }

    /// <summary>Projection lifecycle status.</summary>
    public string? ProjectionStatus { get; init; }

    /// <summary>Successful projection refresh timestamp used for freshness enforcement.</summary>
    public DateTimeOffset? ProjectionLastRefreshedAt { get; init; }

    /// <summary>Age of the projection refresh in seconds.</summary>
    public double? ProjectionFreshnessAgeSeconds { get; init; }
}

/// <summary>
/// APT-safe completion summary for an applied zero-payable statutory benefit.
/// </summary>
public sealed record AptZeroPayableStatutoryCompletionDto(
    string CompletionBasis,
    bool FiscalPrerequisiteSatisfied,
    Guid? FiscalIssuanceReferenceId,
    string? FiscalIssuanceState,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    Guid? ExitAuthorizationId,
    string? ExitAuthorizationStatus,
    DateTimeOffset? ExitAuthorizationIssuedAt,
    DateTimeOffset? ExitAuthorizationExpiresAt);

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
