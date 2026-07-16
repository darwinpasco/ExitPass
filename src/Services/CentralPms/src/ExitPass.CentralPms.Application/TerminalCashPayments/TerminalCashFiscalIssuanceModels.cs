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
}

/// <summary>
/// Terminal cash fiscal issuance command.
/// </summary>
public sealed record TerminalCashFiscalIssuanceCommand(
    Guid TerminalCashTenderId,
    string IdempotencyKey,
    Guid CorrelationId);

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
