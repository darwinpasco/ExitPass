namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for looking up a parking session through the Operator Console.
/// </summary>
public sealed record OperatorConsoleSessionLookupRequest(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    Guid? ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    string? LookupMode,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Response body for an access-gated Operator Console session lookup.
/// </summary>
public sealed record OperatorConsoleSessionLookupResponse(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool SessionFound,
    bool SessionEligible,
    string? IneligibilityReason,
    Guid? ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? SessionStatus,
    DateTimeOffset? EntryTime,
    long? CurrentPayableAmountMinorUnits,
    string? CurrencyCode,
    string? PaymentStatus,
    string? DiscountStatus,
    string? ExitAuthorizationStatus,
    IReadOnlyList<string> Alerts,
    Guid CorrelationId);
