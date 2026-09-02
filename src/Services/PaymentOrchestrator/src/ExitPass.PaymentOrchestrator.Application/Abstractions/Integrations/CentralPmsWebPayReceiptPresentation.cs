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

public sealed record CentralPmsWebPayPaymentAttemptStatus(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid SiteGroupId,
    Guid SiteId,
    string? SiteGroupName,
    string? SiteName,
    string? TicketReference,
    string? PlateNumber,
    long AmountMinorUnits,
    string Currency,
    string? PaymentMethod,
    string? PaymentProvider,
    string? PaymentReference,
    DateTimeOffset? EntryTime,
    DateTimeOffset? PaymentTime,
    string PaymentStatus,
    string ParkingStatus,
    Guid? ExitAuthorizationId,
    string? ExitAuthorizationStatus,
    DateTimeOffset? ExitAuthorizationExpiresAt,
    Guid CorrelationId);
