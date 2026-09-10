using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Application boundary for terminal cash-payment fiscal issuance.
/// </summary>
public interface ITerminalCashFiscalIssuanceService
{
    /// <summary>
    /// Starts or replays fiscal issuance for a confirmed terminal cash payment.
    /// </summary>
    Task<TerminalCashFiscalIssuanceResult> IssueOrReadAsync(
        TerminalCashFiscalIssuanceCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the durable fiscal issuance status linked to a terminal cash tender.
    /// </summary>
    Task<TerminalCashFiscalIssuanceResult?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        Guid? correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recovers one unchanged terminal-cash fiscal obligation whose persisted configuration
    /// failure is explicitly approved for guarded retry. This is not a general retry API.
    /// </summary>
    Task<TerminalCashFiscalConflictRecoveryResult> RecoverConfigurationFailureAsync(
        TerminalCashFiscalConflictRecoveryCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Terminal cash fiscal issuance command.
/// </summary>
public sealed record TerminalCashFiscalIssuanceCommand(
    Guid TerminalCashTenderId,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Explicit single-record recovery request. Expected values are operator-supplied guards and
/// must match the current durable payment and fiscal facts exactly.
/// </summary>
public sealed record TerminalCashFiscalConflictRecoveryCommand(
    Guid TerminalCashTenderId,
    Guid FiscalIssuanceReferenceId,
    Guid ExpectedPaymentAttemptId,
    Guid ExpectedPaymentConfirmationId,
    Guid ExpectedParkingSessionId,
    Guid ExpectedTariffSnapshotId,
    long ExpectedAmountMinorUnits,
    string ExpectedCurrency,
    string ExpectedDeliveryRequestHash,
    string ExpectedFiscalSemanticRequestHash,
    string ExpectedUpstreamFinalityReference,
    string DeliveryIdempotencyKey,
    Guid ExpectedTransactionCorrelationId,
    Guid ExpectedFiscalCorrelationId,
    Guid RecoveryCorrelationId,
    Guid ActorServiceIdentityId,
    string ApprovalReference,
    string ReasonCode,
    string SafeJustification);

/// <summary>
/// Audited result of a governed retryable-configuration recovery.
/// </summary>
public sealed record TerminalCashFiscalConflictRecoveryResult(
    Guid RecoveryAuditId,
    string RecoveryStatus,
    bool RecoveryExecuted,
    bool IdempotentReadback,
    TerminalCashFiscalIssuanceResult FiscalIssuance);

/// <summary>
/// Durable payment/exit facts used to fail closed before fiscal recovery.
/// </summary>
public sealed record TerminalCashFiscalConflictRecoveryFacts(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    string PaymentAttemptStatus,
    string PaymentConfirmationStatus,
    string PaymentAttemptCurrency,
    long PaymentAttemptAmountMinorUnits,
    string PaymentConfirmationCurrency,
    long PaymentConfirmationAmountMinorUnits,
    int ExitAuthorizationCount);

public interface ITerminalCashFiscalConflictRecoveryGuardRepository
{
    Task<TerminalCashFiscalConflictRecoveryFacts?> ReadAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        CancellationToken cancellationToken);
}

public interface ITerminalCashFiscalConflictRecoveryLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Safe terminal cash fiscal issuance response.
/// </summary>
public sealed record TerminalCashFiscalIssuanceResult(
    Guid TerminalCashTenderId,
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid FiscalIssuanceReferenceId,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    FiscalIssuanceResultClassification? ResultClassification,
    Guid? PosFiscalDocumentId,
    string? FiscalDocumentNumber,
    DateTimeOffset? FiscalNumberAssignedAt,
    string? SemanticHashSourceVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CorrelationId,
    string? SafeErrorCode,
    string? SafeErrorPosture,
    bool PosServerCallAttempted,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered);

/// <summary>
/// Controlled terminal cash fiscal issuance rejection.
/// </summary>
public sealed class TerminalCashFiscalIssuanceRejectedException : Exception
{
    /// <summary>
    /// Creates a controlled terminal cash fiscal issuance rejection.
    /// </summary>
    public TerminalCashFiscalIssuanceRejectedException(string errorCode, string message, bool isNotFound = false)
        : base(message)
    {
        ErrorCode = !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : throw new ArgumentException("Error code is required.", nameof(errorCode));
        IsNotFound = isNotFound;
    }

    /// <summary>
    /// Stable safe error code.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// True when the rejection should be mapped to HTTP 404.
    /// </summary>
    public bool IsNotFound { get; }
}
