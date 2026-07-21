using System.Text.Json;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Application boundary for terminal cash-payment receipt-presentation readback.
/// </summary>
public interface ITerminalCashReceiptPresentationService
{
    /// <summary>
    /// Reads the POS Server-owned receipt presentation linked to a recorded terminal cash fiscal document.
    /// </summary>
    Task<TerminalCashReceiptPresentationResult> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Safe Central PMS wrapper for POS Server-owned receipt presentation.
/// </summary>
public sealed record TerminalCashReceiptPresentationResult(
    Guid TerminalCashTenderId,
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    string CanonicalPaymentStatus,
    Guid FiscalIssuanceReferenceId,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    Guid PosFiscalDocumentId,
    string? FiscalDocumentNumber,
    string? FiscalDocumentStatus,
    string ReceiptAvailabilityState,
    string? PresentationVersion,
    string? TemplateVersion,
    string? SemanticRequestHash,
    string? SemanticRequestHashVersion,
    string? SemanticRequestHashStatus,
    string? ContentType,
    JsonElement AuthoritativePresentation,
    string? VoidStatus,
    string? VoidReasonCode,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CorrelationId);

/// <summary>
/// Controlled terminal cash receipt-presentation rejection.
/// </summary>
public sealed class TerminalCashReceiptPresentationRejectedException : Exception
{
    /// <summary>
    /// Creates a controlled terminal cash receipt-presentation rejection.
    /// </summary>
    public TerminalCashReceiptPresentationRejectedException(
        string errorCode,
        string message,
        int httpStatusCode,
        bool retryable)
        : base(message)
    {
        ErrorCode = !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : throw new ArgumentException("Error code is required.", nameof(errorCode));
        HttpStatusCode = httpStatusCode;
        Retryable = retryable;
    }

    /// <summary>
    /// Stable safe error code.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// HTTP status code for the safe error response.
    /// </summary>
    public int HttpStatusCode { get; }

    /// <summary>
    /// Indicates whether the receipt read can be safely retried by the terminal.
    /// </summary>
    public bool Retryable { get; }
}
