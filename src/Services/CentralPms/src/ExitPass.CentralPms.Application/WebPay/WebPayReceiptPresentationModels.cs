using System.Text.Json;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.WebPay;

/// <summary>
/// Application boundary for WebPay receipt-presentation readback.
/// </summary>
public interface IWebPayReceiptPresentationService
{
    /// <summary>
    /// Reads the POS Server-owned receipt presentation linked to a WebPay payment attempt.
    /// </summary>
    Task<WebPayReceiptPresentationResult> GetByPaymentAttemptIdAsync(
        Guid paymentAttemptId,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Safe Central PMS wrapper for POS Server-owned WebPay receipt presentation.
/// </summary>
public sealed record WebPayReceiptPresentationResult(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid FiscalIssuanceReferenceId,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    Guid PosFiscalDocumentId,
    string? FiscalDocumentNumber,
    string? FiscalDocumentStatus,
    string ReceiptAvailabilityState,
    string? PresentationVersion,
    string? TemplateVersion,
    string? ContentType,
    JsonElement AuthoritativePresentation,
    string? VoidStatus,
    string? VoidReasonCode,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CorrelationId);

/// <summary>
/// Controlled WebPay receipt-presentation rejection.
/// </summary>
public sealed class WebPayReceiptPresentationRejectedException : Exception
{
    public WebPayReceiptPresentationRejectedException(
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

    public string ErrorCode { get; }

    public int HttpStatusCode { get; }

    public bool Retryable { get; }
}
