using System.Text.Json;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// POS Server-owned Sales Invoice presentation read through Central PMS for WebPay.
/// </summary>
public sealed record CentralPmsWebPayReceiptPresentation(
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
