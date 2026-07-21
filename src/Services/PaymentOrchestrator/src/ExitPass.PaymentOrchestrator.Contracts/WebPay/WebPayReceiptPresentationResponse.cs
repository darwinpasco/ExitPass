using System.Text.Json;

namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay receipt-presentation readback for the authoritative POS Server-owned Sales Invoice.
/// </summary>
public sealed record WebPayReceiptPresentationResponse(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid FiscalIssuanceReferenceId,
    string FiscalIssuanceState,
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
