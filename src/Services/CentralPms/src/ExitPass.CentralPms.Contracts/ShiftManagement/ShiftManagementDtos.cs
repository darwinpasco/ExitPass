namespace ExitPass.CentralPms.Contracts.ShiftManagement;

public sealed record StartOwnShiftRequest(Guid SiteId, string? TerminalReference);
public sealed record SupervisorCloseShiftRequest(string? Reason);

public sealed record AuthorizedShiftSiteResponse(
    Guid SiteId,
    Guid SiteGroupId,
    string SiteCode,
    string SiteName,
    string SiteGroupCode,
    string SiteGroupName);

public sealed record ShiftSummaryResponse(
    Guid ShiftId,
    string ShiftReference,
    Guid OperatorUserId,
    string Username,
    string DisplayName,
    string UserType,
    IReadOnlyList<string> Roles,
    Guid SiteId,
    Guid SiteGroupId,
    string SiteCode,
    string SiteName,
    string SiteGroupCode,
    string SiteGroupName,
    Guid? DeviceId,
    string? DeviceName,
    string? TerminalReference,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    long ElapsedSeconds,
    string Status,
    string CashCustodyStatus,
    long? OpeningCashMinorUnits,
    int CashTransactionCount,
    long? CashCollectedMinorUnits,
    string? CloseType,
    Guid? ClosedByUserId,
    string? ClosingActorName,
    string? CloseReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ShiftListResponse(IReadOnlyList<ShiftSummaryResponse> Items, Guid CorrelationId);
public sealed record ShiftOperationResponse(bool Succeeded, ShiftSummaryResponse? Shift, string? ErrorCode, Guid CorrelationId);
