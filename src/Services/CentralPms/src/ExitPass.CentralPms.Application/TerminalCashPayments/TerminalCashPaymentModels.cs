namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Application service for terminal cash-payment command and readback.
/// </summary>
public interface ITerminalCashPaymentService
{
    /// <summary>
    /// Creates or reuses a terminal cash payment command and canonical payment confirmation.
    /// </summary>
    Task<TerminalCashPaymentResult> CreateOrReadAsync(
        TerminalCashPaymentCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a terminal cash payment command by terminal cash tender reference.
    /// </summary>
    Task<TerminalCashPaymentReadback?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Repository boundary for durable terminal cash payment state.
/// </summary>
public interface ITerminalCashPaymentRepository
{
    /// <summary>
    /// Creates or reuses terminal cash payment state.
    /// </summary>
    Task<TerminalCashPaymentResult> CreateOrReadAsync(
        TerminalCashPaymentRepositoryCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads terminal cash payment state by terminal cash tender reference.
    /// </summary>
    Task<TerminalCashPaymentReadback?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Terminal cash payment command accepted by Central PMS.
/// </summary>
public sealed record TerminalCashPaymentCommand(
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
    IReadOnlyList<TerminalCashDenominationEntry> DenominationEntries,
    string LocalEventReference,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Normalized repository command with durable semantic hash evidence.
/// </summary>
public sealed record TerminalCashPaymentRepositoryCommand(
    TerminalCashPaymentCommand Command,
    string IdempotencyScope,
    string SemanticRequestHash,
    string SemanticHashSourceVersion,
    DateTimeOffset RequestedAt);

/// <summary>
/// Optional terminal cash denomination evidence.
/// </summary>
public sealed record TerminalCashDenominationEntry(
    string DenominationCode,
    long DenominationValueMinorUnits,
    int Quantity);

/// <summary>
/// Result of a terminal cash payment command.
/// </summary>
public sealed record TerminalCashPaymentResult(
    Guid TerminalCashPaymentCommandId,
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
/// Durable terminal cash payment readback.
/// </summary>
public sealed record TerminalCashPaymentReadback(
    Guid TerminalCashPaymentCommandId,
    Guid TerminalCashTenderId,
    Guid PaymentAttemptId,
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

/// <summary>
/// Controlled terminal cash payment rejection.
/// </summary>
public sealed class TerminalCashPaymentRejectedException : Exception
{
    /// <summary>
    /// Creates a controlled terminal cash payment rejection.
    /// </summary>
    public TerminalCashPaymentRejectedException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : throw new ArgumentException("Error code is required.", nameof(errorCode));
    }

    /// <summary>
    /// Stable error code.
    /// </summary>
    public string ErrorCode { get; }
}
