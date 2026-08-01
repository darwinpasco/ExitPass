namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Central PMS request for WebPay-safe rediscovery of an existing statutory pending lifecycle.
/// </summary>
public sealed record CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest(
    string LookupMode,
    Guid? ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string? VendorSystemId,
    string? EntitlementType);

/// <summary>
/// Central PMS WebPay-safe rediscovery response for an existing statutory pending lifecycle.
/// </summary>
public sealed record CentralPmsStatutoryDiscountPendingLifecycleRediscovery(
    string Classification,
    Guid? StatutoryDecisionId,
    Guid? StatutoryDecisionCommandId,
    Guid? RequestReference,
    string? EntitlementType,
    string? DecisionStatus,
    string? PayableBasisStatus,
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? OpaqueContinuationReference,
    string? OpaqueContinuationUrl,
    string LifecycleState,
    bool Retryable,
    Guid CorrelationId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ReviewedAt);
