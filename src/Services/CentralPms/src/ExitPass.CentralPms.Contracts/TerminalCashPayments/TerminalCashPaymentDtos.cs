namespace ExitPass.CentralPms.Contracts.TerminalCashPayments;

/// <summary>
/// Terminal cash payment command submitted to Central PMS.
/// </summary>
public sealed record TerminalCashPaymentRequest(
    Guid TerminalCashTenderId,
    Guid CashCustodySessionId,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    string CashierId,
    string CashierSessionReference,
    string CashierShiftId,
    string TerminalId,
    Guid SiteId,
    Guid SiteGroupId,
    string PosServerId,
    string Currency,
    long AmountDueMinorUnits,
    long AmountTenderedMinorUnits,
    long ChangeDueMinorUnits,
    DateTimeOffset CashReceivedAt,
    IReadOnlyList<TerminalCashDenominationEntryDto>? DenominationEntries,
    string LocalEventReference);

/// <summary>
/// Optional cash denomination evidence line.
/// </summary>
public sealed record TerminalCashDenominationEntryDto(
    string DenominationCode,
    long DenominationValueMinorUnits,
    int Quantity);

/// <summary>
/// Terminal cash payment command response.
/// </summary>
public sealed record TerminalCashPaymentResponse(
    Guid TerminalCashTenderId,
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    string CanonicalPaymentStatus,
    string ResultClassification,
    string IdempotencyScope,
    string SemanticHashSourceVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset LastUpdatedAt,
    Guid CorrelationId,
    string FiscalStatus);

/// <summary>
/// Terminal cash payment durable status readback.
/// </summary>
public sealed record TerminalCashPaymentReadbackResponse(
    Guid TerminalCashTenderId,
    Guid CashCustodySessionId,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    string TerminalId,
    Guid SiteId,
    Guid SiteGroupId,
    string PosServerId,
    string CashierId,
    string CashierShiftId,
    string Currency,
    long AmountDueMinorUnits,
    long AmountTenderedMinorUnits,
    long ChangeDueMinorUnits,
    string CanonicalPaymentStatus,
    Guid PaymentConfirmationId,
    string ResultClassification,
    string IdempotencyScope,
    string SemanticHashSourceVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset LastUpdatedAt,
    Guid CorrelationId,
    string FiscalStatus);
