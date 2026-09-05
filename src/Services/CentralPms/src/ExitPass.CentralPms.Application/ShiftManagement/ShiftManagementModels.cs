namespace ExitPass.CentralPms.Application.ShiftManagement;

public static class ShiftManagementPermissions
{
    public const string OperateOwn = "cashier-shifts.operate";
    public const string View = "shift-management.view";
    public const string Manage = "shift-management.manage";
}

public static class ShiftManagementFailureCodes
{
    public const string UserInactive = "SHIFT_USER_INACTIVE";
    public const string PermissionDenied = "SHIFT_PERMISSION_DENIED";
    public const string SiteNotAuthorized = "SHIFT_SITE_NOT_AUTHORIZED";
    public const string DeviceSiteMismatch = "SHIFT_DEVICE_SITE_MISMATCH";
    public const string ActiveShiftAlreadyExists = "ACTIVE_SHIFT_ALREADY_EXISTS";
    public const string ShiftNotFound = "SHIFT_NOT_FOUND";
    public const string ShiftNotOwned = "SHIFT_NOT_OWNED";
    public const string ShiftNotActive = "SHIFT_NOT_ACTIVE";
    public const string CloseBlockedByOpenCustody = "SHIFT_CLOSE_BLOCKED_BY_OPEN_CUSTODY";
    public const string CloseReasonRequired = "SHIFT_CLOSE_REASON_REQUIRED";
}

public sealed record ShiftManagementActor(
    Guid UserId,
    Guid? DeviceServiceIdentityId,
    Guid? OperatorDeviceBindingId,
    Guid CorrelationId);

public sealed record ShiftAuthorizedSite(
    Guid SiteId,
    Guid SiteGroupId,
    string SiteCode,
    string SiteName,
    string SiteGroupCode,
    string SiteGroupName);

public sealed record ShiftActorAccess(
    bool UserActive,
    string Username,
    string DisplayName,
    string UserType,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<ShiftAuthorizedSite> Sites);

public sealed record ShiftSummary(
    Guid ShiftId,
    string ShiftReference,
    Guid OperatorUserId,
    string Username,
    string DisplayName,
    string UserType,
    IReadOnlyList<string> RoleCodes,
    Guid SiteId,
    Guid SiteGroupId,
    string SiteCode,
    string SiteName,
    string SiteGroupCode,
    string SiteGroupName,
    Guid? OperatorDeviceBindingId,
    string? DeviceName,
    string? TerminalReference,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
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

public sealed record StartOwnShiftCommand(
    ShiftManagementActor Actor,
    Guid SiteId,
    string? TerminalReference);

public sealed record ShiftListQuery(
    ShiftManagementActor Actor,
    string View,
    Guid? SiteId,
    Guid? StaffUserId,
    int Limit);

public sealed record ShiftOperationResult(
    bool Succeeded,
    ShiftSummary? Shift,
    string? ErrorCode,
    Guid CorrelationId)
{
    public static ShiftOperationResult Success(ShiftSummary shift, Guid correlationId) =>
        new(true, shift, null, correlationId);

    public static ShiftOperationResult Failure(string errorCode, Guid correlationId, ShiftSummary? shift = null) =>
        new(false, shift, errorCode, correlationId);
}

public interface IShiftManagementRepository
{
    Task<ShiftActorAccess?> ReadAccessAsync(Guid userId, string permission, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> DeviceMatchesSiteAsync(ShiftManagementActor actor, Guid siteId, Guid siteGroupId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ShiftSummary?> ReadCurrentOwnAsync(Guid userId, CancellationToken cancellationToken);
    Task<ShiftSummary?> ReadByIdAsync(Guid shiftId, CancellationToken cancellationToken);
    Task<ShiftSummary> InsertAsync(StartOwnShiftCommand command, ShiftAuthorizedSite site, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ShiftSummary?> RecordResumeAsync(Guid shiftId, Guid actorUserId, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ShiftSummary?> CloseAsync(Guid shiftId, Guid actorUserId, string closeType, string? reason, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShiftSummary>> ListAsync(IReadOnlyList<Guid> authorizedSiteIds, string view, Guid? siteId, Guid? staffUserId, int limit, CancellationToken cancellationToken);
    Task RecordDenialAsync(Guid actorUserId, Guid? shiftId, Guid? siteId, string reasonCode, string action, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IShiftManagementService
{
    Task<IReadOnlyList<ShiftAuthorizedSite>> ListAuthorizedSitesAsync(ShiftManagementActor actor, CancellationToken cancellationToken);
    Task<ShiftSummary?> GetCurrentOwnAsync(ShiftManagementActor actor, CancellationToken cancellationToken);
    Task<ShiftOperationResult> StartOwnAsync(StartOwnShiftCommand command, CancellationToken cancellationToken);
    Task<ShiftOperationResult> ResumeOwnAsync(ShiftManagementActor actor, Guid shiftId, CancellationToken cancellationToken);
    Task<ShiftOperationResult> CloseOwnAsync(ShiftManagementActor actor, Guid shiftId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShiftSummary>> ListAsync(ShiftListQuery query, CancellationToken cancellationToken);
    Task<ShiftOperationResult> GetAsync(ShiftManagementActor actor, Guid shiftId, CancellationToken cancellationToken);
    Task<ShiftOperationResult> ExceptionCloseAsync(ShiftManagementActor actor, Guid shiftId, string? reason, CancellationToken cancellationToken);
}

public sealed class ActiveShiftConflictException(ShiftSummary existingShift) : Exception(ShiftManagementFailureCodes.ActiveShiftAlreadyExists)
{
    public ShiftSummary ExistingShift { get; } = existingShift;
}
