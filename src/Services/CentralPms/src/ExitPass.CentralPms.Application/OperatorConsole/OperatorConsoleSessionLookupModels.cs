namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Command for an access-gated Operator Console parking session lookup.
/// </summary>
public sealed record OperatorConsoleSessionLookupCommand(
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
/// Result for an access-gated Operator Console parking session lookup.
/// </summary>
public sealed record OperatorConsoleSessionLookupResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    OperatorConsoleSessionReadModel? Session,
    bool SessionEligible,
    string? IneligibilityReason,
    IReadOnlyList<string> Alerts,
    Guid CorrelationId);

/// <summary>
/// Read-only Operator Console parking session lookup query.
/// </summary>
public sealed record OperatorConsoleSessionLookupReadRequest(
    Guid? ParkingSessionId,
    string? TicketReference,
    Guid? SiteId,
    Guid? SiteGroupId,
    string LookupMode);

/// <summary>
/// Read-only parking session context returned to Operator Console callers.
/// </summary>
public sealed record OperatorConsoleSessionReadModel(
    Guid ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    Guid SiteId,
    Guid SiteGroupId,
    string SessionStatus,
    DateTimeOffset EntryTime,
    long? CurrentPayableAmountMinorUnits,
    string? CurrencyCode,
    string? PaymentStatus,
    string? DiscountStatus,
    string? ExitAuthorizationStatus);
