using ExitPass.CentralPms.Application.ShiftManagement;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ShiftManagementServiceTests
{
    private static readonly Guid UserId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    private static readonly Guid SupervisorId = Guid.Parse("61000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("61000000-0000-0000-0000-000000000003");
    private static readonly Guid OtherSiteId = Guid.Parse("61000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("61000000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T04:00:00Z");

    [Fact]
    public async Task StartOwn_AllowsAuthorizedSiteWithoutScheduleAndStoresExactlyOneSite()
    {
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId) };
        var result = await Sut(repository).StartOwnAsync(new StartOwnShiftCommand(Actor(UserId), SiteId, null), default);

        result.Succeeded.Should().BeTrue();
        result.Shift!.SiteId.Should().Be(SiteId);
        result.Shift.SiteGroupId.Should().Be(SiteGroupId);
        repository.InsertCount.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizedSites_NoSiteGrant_ReturnsControlledEmptyState()
    {
        var repository = new FakeRepository { OwnAccess = Access(UserId) };
        (await Sut(repository).ListAuthorizedSitesAsync(Actor(UserId), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizedSites_SupervisorViewScope_IsAvailableWithoutCashierPermission()
    {
        var repository = new FakeRepository { ViewAccess = Access(SupervisorId, SiteId, OtherSiteId) };
        var sites = await Sut(repository).ListAuthorizedSitesAsync(Actor(SupervisorId), default);
        sites.Select(site => site.SiteId).Should().BeEquivalentTo([SiteId, OtherSiteId]);
    }

    [Fact]
    public async Task StartOwn_UnauthorizedBrowserSite_IsDeniedAndAudited()
    {
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId) };
        var result = await Sut(repository).StartOwnAsync(new StartOwnShiftCommand(Actor(UserId), OtherSiteId, null), default);

        result.ErrorCode.Should().Be(ShiftManagementFailureCodes.SiteNotAuthorized);
        repository.Denials.Should().ContainSingle(value => value.Reason == ShiftManagementFailureCodes.SiteNotAuthorized);
        repository.InsertCount.Should().Be(0);
    }

    [Fact]
    public async Task StartOwn_ExistingShiftAtAnySite_PreventsOverlap()
    {
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId, OtherSiteId), Current = Shift(SiteId) };
        var result = await Sut(repository).StartOwnAsync(new StartOwnShiftCommand(Actor(UserId), OtherSiteId, null), default);

        result.ErrorCode.Should().Be(ShiftManagementFailureCodes.ActiveShiftAlreadyExists);
        result.Shift!.SiteId.Should().Be(SiteId);
    }

    [Fact]
    public async Task StartOwn_DeviceSiteMismatch_IsDenied()
    {
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId), DeviceMatches = false };
        var result = await Sut(repository).StartOwnAsync(new StartOwnShiftCommand(Actor(UserId, device: Guid.NewGuid()), SiteId, null), default);
        result.ErrorCode.Should().Be(ShiftManagementFailureCodes.DeviceSiteMismatch);
    }

    [Fact]
    public async Task CloseOwn_OpenCustody_IsBlockedWithoutAutomaticCustodyClose()
    {
        var open = Shift(SiteId) with { CashCustodyStatus = "OPEN" };
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId), Current = open, ById = open };
        var result = await Sut(repository).CloseOwnAsync(Actor(UserId), open.ShiftId, default);

        result.ErrorCode.Should().Be(ShiftManagementFailureCodes.CloseBlockedByOpenCustody);
        repository.CloseCount.Should().Be(0);
        repository.ById!.CashCustodyStatus.Should().Be("OPEN");
    }

    [Fact]
    public async Task ResumeOwn_ReusesOwnedActiveShift()
    {
        var active = Shift(SiteId);
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId), ById = active };
        var result = await Sut(repository).ResumeOwnAsync(Actor(UserId), active.ShiftId, default);

        result.Succeeded.Should().BeTrue();
        result.Shift!.ShiftId.Should().Be(active.ShiftId);
        repository.ResumeCount.Should().Be(1);
        repository.InsertCount.Should().Be(0);
    }

    [Fact]
    public async Task CloseOwn_ClosedCustody_ClosesNormally()
    {
        var active = Shift(SiteId);
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId), ById = active };
        var result = await Sut(repository).CloseOwnAsync(Actor(UserId), active.ShiftId, default);

        result.Succeeded.Should().BeTrue();
        result.Shift!.CloseType.Should().Be("NORMAL");
        repository.CloseCount.Should().Be(1);
    }

    [Fact]
    public async Task ExceptionClose_RequiresReasonAndCurrentSupervisorSiteScope()
    {
        var shift = Shift(SiteId);
        var repository = new FakeRepository { ManageAccess = Access(SupervisorId, SiteId), ById = shift };
        var noReason = await Sut(repository).ExceptionCloseAsync(Actor(SupervisorId), shift.ShiftId, " ", default);
        noReason.ErrorCode.Should().Be(ShiftManagementFailureCodes.CloseReasonRequired);

        repository.ManageAccess = Access(SupervisorId, OtherSiteId);
        var outOfScope = await Sut(repository).ExceptionCloseAsync(Actor(SupervisorId), shift.ShiftId, "Abandoned terminal", default);
        outOfScope.ErrorCode.Should().Be(ShiftManagementFailureCodes.SiteNotAuthorized);
        repository.CloseCount.Should().Be(0);
    }

    [Fact]
    public async Task ExceptionClose_AuthorizedSupervisor_RecordsExceptionReason()
    {
        var shift = Shift(SiteId);
        var repository = new FakeRepository { ManageAccess = Access(SupervisorId, SiteId), ById = shift };
        var result = await Sut(repository).ExceptionCloseAsync(Actor(SupervisorId), shift.ShiftId, "Abandoned terminal", default);

        result.Succeeded.Should().BeTrue();
        result.Shift!.CloseType.Should().Be("SUPERVISOR_EXCEPTION");
        result.Shift.CloseReason.Should().Be("Abandoned terminal");
        repository.CloseCount.Should().Be(1);
    }

    [Fact]
    public async Task ExceptionClose_OrdinaryOperatorWithoutManagePermission_IsDenied()
    {
        var shift = Shift(SiteId);
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId), ById = shift };
        var result = await Sut(repository).ExceptionCloseAsync(Actor(UserId), shift.ShiftId, "Abandoned terminal", default);

        result.ErrorCode.Should().Be(ShiftManagementFailureCodes.PermissionDenied);
        repository.CloseCount.Should().Be(0);
        repository.Denials.Should().ContainSingle(value => value.Action == "EXCEPTION_CLOSE");
    }

    [Fact]
    public async Task List_UsesOnlyCurrentSupervisorSiteScope()
    {
        var repository = new FakeRepository { ViewAccess = Access(SupervisorId, SiteId) };
        await Sut(repository).ListAsync(new ShiftListQuery(Actor(SupervisorId), "OPEN", OtherSiteId, null, 50), default);

        repository.LastListAuthorizedSiteIds.Should().Equal(SiteId);
        repository.LastListRequestedSiteId.Should().Be(OtherSiteId);
        repository.LastListRequestedStaffUserId.Should().BeNull();
    }

    [Fact]
    public async Task List_OwnOperatorRecentlyClosed_ForcesAuthenticatedUserFilter()
    {
        var closed = Shift(SiteId) with { Status = "ENDED", ClosedAt = Now, CloseType = "NORMAL" };
        var repository = new FakeRepository
        {
            OwnAccess = Access(UserId, SiteId),
            ListResult = [closed]
        };

        var result = await Sut(repository).ListAsync(
            new ShiftListQuery(Actor(UserId), "RECENTLY_CLOSED", SiteId, SupervisorId, 50),
            default);

        result.Should().ContainSingle().Which.OperatorUserId.Should().Be(UserId);
        repository.LastListAuthorizedSiteIds.Should().Equal(SiteId);
        repository.LastListRequestedSiteId.Should().Be(SiteId);
        repository.LastListRequestedStaffUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Get_OwnOperatorClosedShift_AllowsOnlyOwnedAuthorizedDetail()
    {
        var owned = Shift(SiteId) with { Status = "ENDED", ClosedAt = Now, CloseType = "NORMAL" };
        var repository = new FakeRepository { OwnAccess = Access(UserId, SiteId), ById = owned };

        var ownResult = await Sut(repository).GetAsync(Actor(UserId), owned.ShiftId, default);
        ownResult.Succeeded.Should().BeTrue();

        repository.ById = owned with { OperatorUserId = SupervisorId };
        var otherResult = await Sut(repository).GetAsync(Actor(UserId), owned.ShiftId, default);
        otherResult.ErrorCode.Should().Be(ShiftManagementFailureCodes.ShiftNotFound);
    }

    [Fact]
    public async Task Get_SupervisorView_RemainsSiteScoped()
    {
        var otherUserShift = Shift(SiteId) with { OperatorUserId = UserId };
        var repository = new FakeRepository { ViewAccess = Access(SupervisorId, SiteId), ById = otherUserShift };

        (await Sut(repository).GetAsync(Actor(SupervisorId), otherUserShift.ShiftId, default)).Succeeded.Should().BeTrue();

        repository.ViewAccess = Access(SupervisorId, OtherSiteId);
        (await Sut(repository).GetAsync(Actor(SupervisorId), otherUserShift.ShiftId, default))
            .ErrorCode.Should().Be(ShiftManagementFailureCodes.ShiftNotFound);
    }

    private static ShiftManagementService Sut(FakeRepository repository) => new(repository, new FixedTimeProvider(Now));
    private static ShiftManagementActor Actor(Guid userId, Guid? device = null) => new(userId, device, null, Guid.NewGuid());

    private static ShiftActorAccess Access(Guid userId, params Guid[] sites) => new(
        true, $"user-{userId:N}", "Test User", "SITE_OPERATOR", ["SITE_OPERATOR"],
        sites.Select(id => new ShiftAuthorizedSite(id, SiteGroupId, $"SITE-{id:N}", "Site", "GROUP", "Group")).ToArray());

    private static ShiftSummary Shift(Guid siteId) => new(
        Guid.NewGuid(), "SHIFT-1", UserId, "cashier", "Test Cashier", "SITE_OPERATOR", ["SITE_OPERATOR"],
        siteId, SiteGroupId, "SITE", "Site", "GROUP", "Group", null, null, null, Now, null, "ACTIVE", "NONE",
        null, 0, null, null, null, null, null, Now, Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRepository : IShiftManagementRepository
    {
        public ShiftActorAccess? OwnAccess { get; set; }
        public ShiftActorAccess? ViewAccess { get; set; }
        public ShiftActorAccess? ManageAccess { get; set; }
        public bool DeviceMatches { get; set; } = true;
        public ShiftSummary? Current { get; set; }
        public ShiftSummary? ById { get; set; }
        public int InsertCount { get; private set; }
        public int CloseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public IReadOnlyList<Guid> LastListAuthorizedSiteIds { get; private set; } = [];
        public Guid? LastListRequestedSiteId { get; private set; }
        public Guid? LastListRequestedStaffUserId { get; private set; }
        public IReadOnlyList<ShiftSummary> ListResult { get; set; } = [];
        public List<(string Reason, string Action)> Denials { get; } = [];

        public Task<ShiftActorAccess?> ReadAccessAsync(Guid userId, string permission, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(permission switch { ShiftManagementPermissions.Manage => ManageAccess, ShiftManagementPermissions.View => ViewAccess, _ => OwnAccess });
        public Task<bool> DeviceMatchesSiteAsync(ShiftManagementActor actor, Guid siteId, Guid siteGroupId, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(DeviceMatches);
        public Task<ShiftSummary?> ReadCurrentOwnAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<ShiftSummary?> ReadByIdAsync(Guid shiftId, CancellationToken cancellationToken) => Task.FromResult(ById ?? Current);
        public Task<ShiftSummary> InsertAsync(StartOwnShiftCommand command, ShiftAuthorizedSite site, DateTimeOffset now, CancellationToken cancellationToken)
        {
            InsertCount++;
            Current = Shift(site.SiteId);
            return Task.FromResult(Current);
        }
        public Task<ShiftSummary?> RecordResumeAsync(Guid shiftId, Guid actorUserId, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ResumeCount++;
            return Task.FromResult(ById ?? Current);
        }
        public Task<ShiftSummary?> CloseAsync(Guid shiftId, Guid actorUserId, string closeType, string? reason, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CloseCount++;
            var closed = (ById ?? Current)! with { Status = "ENDED", ClosedAt = now, CloseType = closeType, CloseReason = reason, ClosedByUserId = actorUserId };
            ById = closed;
            return Task.FromResult<ShiftSummary?>(closed);
        }
        public Task<IReadOnlyList<ShiftSummary>> ListAsync(IReadOnlyList<Guid> authorizedSiteIds, string view, Guid? siteId, Guid? staffUserId, int limit, CancellationToken cancellationToken)
        {
            LastListAuthorizedSiteIds = authorizedSiteIds;
            LastListRequestedSiteId = siteId;
            LastListRequestedStaffUserId = staffUserId;
            return Task.FromResult(ListResult);
        }
        public Task RecordDenialAsync(Guid actorUserId, Guid? shiftId, Guid? siteId, string reasonCode, string action, Guid correlationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Denials.Add((reasonCode, action));
            return Task.CompletedTask;
        }
    }
}
