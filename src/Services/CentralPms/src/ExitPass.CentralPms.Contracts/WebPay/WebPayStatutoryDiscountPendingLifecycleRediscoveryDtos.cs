namespace ExitPass.CentralPms.Contracts.WebPay;

/// <summary>
/// WebPay-safe request for rediscovering an existing statutory-discount pending lifecycle.
/// </summary>
public sealed record WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
    string LookupMode,
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string? VendorSystemId,
    string? EntitlementType);

/// <summary>
/// WebPay-safe response for existing statutory-discount pending lifecycle rediscovery.
/// </summary>
public sealed record WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse(
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
