namespace ExitPass.CentralPms.Application.ShiftManagement;

public sealed class ShiftManagementService(
    IShiftManagementRepository repository,
    TimeProvider timeProvider) : IShiftManagementService
{
    public async Task<IReadOnlyList<ShiftAuthorizedSite>> ListAuthorizedSitesAsync(
        ShiftManagementActor actor,
        CancellationToken cancellationToken)
    {
        var now = Now;
        var operateAccess = await repository.ReadAccessAsync(actor.UserId, ShiftManagementPermissions.OperateOwn, now, cancellationToken);
        var viewAccess = await repository.ReadAccessAsync(actor.UserId, ShiftManagementPermissions.View, now, cancellationToken);
        return new[] { operateAccess, viewAccess }
            .Where(access => access is { UserActive: true })
            .SelectMany(access => access!.Sites)
            .DistinctBy(site => site.SiteId)
            .OrderBy(site => site.SiteName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<ShiftSummary?> GetCurrentOwnAsync(ShiftManagementActor actor, CancellationToken cancellationToken) =>
        repository.ReadCurrentOwnAsync(actor.UserId, cancellationToken);

    public async Task<ShiftOperationResult> StartOwnAsync(StartOwnShiftCommand command, CancellationToken cancellationToken)
    {
        var now = Now;
        var access = await repository.ReadAccessAsync(command.Actor.UserId, ShiftManagementPermissions.OperateOwn, now, cancellationToken);
        var site = access?.Sites.SingleOrDefault(value => value.SiteId == command.SiteId);
        var error = access switch
        {
            null => ShiftManagementFailureCodes.PermissionDenied,
            { UserActive: false } => ShiftManagementFailureCodes.UserInactive,
            _ when site is null => ShiftManagementFailureCodes.SiteNotAuthorized,
            _ => null
        };

        if (error is null && !await repository.DeviceMatchesSiteAsync(command.Actor, site!.SiteId, site.SiteGroupId, now, cancellationToken))
        {
            error = ShiftManagementFailureCodes.DeviceSiteMismatch;
        }

        if (error is not null)
        {
            await Deny(command.Actor, null, command.SiteId, error, "START", now, cancellationToken);
            return ShiftOperationResult.Failure(error, command.Actor.CorrelationId);
        }

        var existing = await repository.ReadCurrentOwnAsync(command.Actor.UserId, cancellationToken);
        if (existing is not null)
        {
            await Deny(command.Actor, existing.ShiftId, command.SiteId, ShiftManagementFailureCodes.ActiveShiftAlreadyExists, "START", now, cancellationToken);
            return ShiftOperationResult.Failure(ShiftManagementFailureCodes.ActiveShiftAlreadyExists, command.Actor.CorrelationId, existing);
        }

        try
        {
            var shift = await repository.InsertAsync(command, site!, now, cancellationToken);
            return ShiftOperationResult.Success(shift, command.Actor.CorrelationId);
        }
        catch (ActiveShiftConflictException conflict)
        {
            await Deny(command.Actor, conflict.ExistingShift.ShiftId, command.SiteId, ShiftManagementFailureCodes.ActiveShiftAlreadyExists, "START", now, cancellationToken);
            return ShiftOperationResult.Failure(ShiftManagementFailureCodes.ActiveShiftAlreadyExists, command.Actor.CorrelationId, conflict.ExistingShift);
        }
    }

    public async Task<ShiftOperationResult> ResumeOwnAsync(ShiftManagementActor actor, Guid shiftId, CancellationToken cancellationToken)
    {
        var validation = await ValidateOwnActiveAsync(actor, shiftId, "RESUME", cancellationToken);
        if (!validation.Succeeded) return validation;
        var resumed = await repository.RecordResumeAsync(shiftId, actor.UserId, actor.CorrelationId, Now, cancellationToken);
        return resumed is null
            ? ShiftOperationResult.Failure(ShiftManagementFailureCodes.ShiftNotActive, actor.CorrelationId)
            : ShiftOperationResult.Success(resumed, actor.CorrelationId);
    }

    public async Task<ShiftOperationResult> CloseOwnAsync(ShiftManagementActor actor, Guid shiftId, CancellationToken cancellationToken)
    {
        var validation = await ValidateOwnActiveAsync(actor, shiftId, "CLOSE", cancellationToken);
        if (!validation.Succeeded) return validation;
        if (validation.Shift!.CashCustodyStatus == "OPEN")
        {
            await Deny(actor, shiftId, validation.Shift.SiteId, ShiftManagementFailureCodes.CloseBlockedByOpenCustody, "CLOSE", Now, cancellationToken);
            return ShiftOperationResult.Failure(ShiftManagementFailureCodes.CloseBlockedByOpenCustody, actor.CorrelationId, validation.Shift);
        }

        var closed = await repository.CloseAsync(shiftId, actor.UserId, "NORMAL", null, actor.CorrelationId, Now, cancellationToken);
        return closed is null
            ? ShiftOperationResult.Failure(ShiftManagementFailureCodes.ShiftNotActive, actor.CorrelationId)
            : ShiftOperationResult.Success(closed, actor.CorrelationId);
    }

    public async Task<IReadOnlyList<ShiftSummary>> ListAsync(ShiftListQuery query, CancellationToken cancellationToken)
    {
        var access = await repository.ReadAccessAsync(query.Actor.UserId, ShiftManagementPermissions.View, Now, cancellationToken);
        if (access is not { UserActive: true }) return [];
        return await repository.ListAsync(access.Sites.Select(site => site.SiteId).ToArray(), query.View, query.SiteId, query.StaffUserId, Math.Clamp(query.Limit, 1, 100), cancellationToken);
    }

    public async Task<ShiftOperationResult> GetAsync(ShiftManagementActor actor, Guid shiftId, CancellationToken cancellationToken)
    {
        var access = await repository.ReadAccessAsync(actor.UserId, ShiftManagementPermissions.View, Now, cancellationToken);
        var shift = await repository.ReadByIdAsync(shiftId, cancellationToken);
        return shift is not null && access is { UserActive: true } && access.Sites.Any(site => site.SiteId == shift.SiteId)
            ? ShiftOperationResult.Success(shift, actor.CorrelationId)
            : ShiftOperationResult.Failure(ShiftManagementFailureCodes.ShiftNotFound, actor.CorrelationId);
    }

    public async Task<ShiftOperationResult> ExceptionCloseAsync(ShiftManagementActor actor, Guid shiftId, string? reason, CancellationToken cancellationToken)
    {
        var now = Now;
        var shift = await repository.ReadByIdAsync(shiftId, cancellationToken);
        var access = await repository.ReadAccessAsync(actor.UserId, ShiftManagementPermissions.Manage, now, cancellationToken);
        var error = shift switch
        {
            null => ShiftManagementFailureCodes.ShiftNotFound,
            _ when access is not { UserActive: true } => ShiftManagementFailureCodes.PermissionDenied,
            _ when !access.Sites.Any(site => site.SiteId == shift.SiteId) => ShiftManagementFailureCodes.SiteNotAuthorized,
            { Status: not "ACTIVE" } => ShiftManagementFailureCodes.ShiftNotActive,
            { CashCustodyStatus: "OPEN" } => ShiftManagementFailureCodes.CloseBlockedByOpenCustody,
            _ when string.IsNullOrWhiteSpace(reason) => ShiftManagementFailureCodes.CloseReasonRequired,
            _ => null
        };

        if (error is not null)
        {
            await Deny(actor, shiftId, shift?.SiteId, error, "EXCEPTION_CLOSE", now, cancellationToken);
            return ShiftOperationResult.Failure(error, actor.CorrelationId, shift);
        }

        var closed = await repository.CloseAsync(shiftId, actor.UserId, "SUPERVISOR_EXCEPTION", reason!.Trim(), actor.CorrelationId, now, cancellationToken);
        return closed is null
            ? ShiftOperationResult.Failure(ShiftManagementFailureCodes.ShiftNotActive, actor.CorrelationId)
            : ShiftOperationResult.Success(closed, actor.CorrelationId);
    }

    private async Task<ShiftOperationResult> ValidateOwnActiveAsync(ShiftManagementActor actor, Guid shiftId, string action, CancellationToken cancellationToken)
    {
        var now = Now;
        var shift = await repository.ReadByIdAsync(shiftId, cancellationToken);
        var access = await repository.ReadAccessAsync(actor.UserId, ShiftManagementPermissions.OperateOwn, now, cancellationToken);
        var error = shift switch
        {
            null => ShiftManagementFailureCodes.ShiftNotFound,
            _ when shift.OperatorUserId != actor.UserId => ShiftManagementFailureCodes.ShiftNotOwned,
            _ when access is not { UserActive: true } => ShiftManagementFailureCodes.PermissionDenied,
            _ when !access.Sites.Any(site => site.SiteId == shift.SiteId) => ShiftManagementFailureCodes.SiteNotAuthorized,
            { Status: not "ACTIVE" } => ShiftManagementFailureCodes.ShiftNotActive,
            _ => null
        };

        if (error is null && !await repository.DeviceMatchesSiteAsync(actor, shift!.SiteId, shift.SiteGroupId, now, cancellationToken))
        {
            error = ShiftManagementFailureCodes.DeviceSiteMismatch;
        }

        if (error is null) return ShiftOperationResult.Success(shift!, actor.CorrelationId);
        await Deny(actor, shiftId, shift?.SiteId, error, action, now, cancellationToken);
        return ShiftOperationResult.Failure(error, actor.CorrelationId, shift);
    }

    private Task Deny(ShiftManagementActor actor, Guid? shiftId, Guid? siteId, string reason, string action, DateTimeOffset now, CancellationToken cancellationToken) =>
        repository.RecordDenialAsync(actor.UserId, shiftId, siteId, reason, action, actor.CorrelationId, now, cancellationToken);

    private DateTimeOffset Now => timeProvider.GetUtcNow();
}
