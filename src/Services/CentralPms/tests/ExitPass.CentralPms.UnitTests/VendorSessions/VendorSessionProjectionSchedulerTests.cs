using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.VendorSessions;

/// <summary>
/// Tests for centralized site-scoped vendor session projection scheduling.
/// </summary>
public sealed class VendorSessionProjectionSchedulerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");

    [Fact]
    public async Task RunDueTargetsOnceAsync_LoadsEnabledDueTargetsAndRunsOneSyncPerTarget()
    {
        var target = Target("LOT-1");
        var repository = new InMemoryTargetRepository([target]);
        var sync = new RecordingSyncService();
        var sut = CreateSut(repository, sync);

        var result = await sut.RunDueTargetsOnceAsync(CancellationToken.None);

        result.TargetsLoaded.Should().Be(1);
        result.TargetsRun.Should().Be(1);
        result.TargetsSucceeded.Should().Be(1);
        sync.Commands.Should().ContainSingle()
            .Which.Should().Match<SyncVendorSessionProjectionsCommand>(command =>
                command.SiteId == target.SiteId &&
                command.SiteGroupId == target.SiteGroupId &&
                command.VendorSystemId == target.VendorSystemId &&
                command.ParkingLotIndexCode == "LOT-1");
        repository.HealthUpdates.Should().ContainSingle(update => update.Succeeded);
    }

    [Fact]
    public async Task RunDueTargetsOnceAsync_SkipsDisabledTargets()
    {
        var repository = new InMemoryTargetRepository([Target("LOT-1", enabled: false)]);
        var sync = new RecordingSyncService();
        var sut = CreateSut(repository, sync);

        var result = await sut.RunDueTargetsOnceAsync(CancellationToken.None);

        result.TargetsLoaded.Should().Be(0);
        sync.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task RunDueTargetsOnceAsync_IsolatesFailurePerTarget()
    {
        var repository = new InMemoryTargetRepository([Target("LOT-1"), Target("LOT-2")]);
        var sync = new RecordingSyncService
        {
            ThrowForParkingLot = "LOT-1"
        };
        var sut = CreateSut(repository, sync);

        var result = await sut.RunDueTargetsOnceAsync(CancellationToken.None);

        result.TargetsRun.Should().Be(2);
        result.TargetsSucceeded.Should().Be(1);
        result.TargetsFailed.Should().Be(1);
        repository.HealthUpdates.Should().Contain(update => update.Succeeded);
        repository.HealthUpdates.Should().Contain(update => !update.Succeeded && update.ErrorCode == nameof(InvalidOperationException));
    }

    [Fact]
    public async Task RunDueTargetsOnceAsync_AppliesLookbackWindowAndPageSize()
    {
        var target = Target("LOT-1") with
        {
            LookbackWindowMinutes = 45,
            PageSize = 25
        };
        var sync = new RecordingSyncService();
        var sut = CreateSut(new InMemoryTargetRepository([target]), sync);

        await sut.RunDueTargetsOnceAsync(CancellationToken.None);

        var command = sync.Commands.Single();
        command.BeginTime.Should().Be(Now.AddMinutes(-45));
        command.EndTime.Should().Be(Now);
        command.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task RunManualAsync_RequiresSiteOrParkingLotScope()
    {
        var sut = CreateSut(new InMemoryTargetRepository([]), new RecordingSyncService());

        var act = () => sut.RunManualAsync(
            new RunVendorSessionProjectionSyncCommand(null, null, null, null, false, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*requires site_id or parking_lot_index_code*");
    }

    [Fact]
    public async Task RunManualAsync_CallsSyncServiceWithScopedTargetAndOverrides()
    {
        var target = Target("LOT-MANUAL");
        var sync = new RecordingSyncService();
        var sut = CreateSut(new InMemoryTargetRepository([target]), sync);

        var result = await sut.RunManualAsync(
            new RunVendorSessionProjectionSyncCommand(target.SiteId, null, 15, 10, true, Guid.Parse("eeeeeeee-0000-0000-0000-000000000001")),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.RecordsRead.Should().Be(3);
        result.RecordsUpserted.Should().Be(2);
        sync.Commands.Should().ContainSingle()
            .Which.Should().Match<SyncVendorSessionProjectionsCommand>(command =>
                command.ParkingLotIndexCode == "LOT-MANUAL" &&
                command.BeginTime == Now.AddMinutes(-15) &&
                command.PageSize == 10);
    }

    [Fact]
    public void FullDdl_IncludesProjectionSyncTargetTableIndexesAndHealthChecks()
    {
        var ddl = File.ReadAllText(FindRepoFile("ExitPass_Full_Database_Creation_DDL_v1.2.sql"));

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS sessions.vendor_session_projection_sync_targets");
        ddl.Should().Contain("ck_vendor_session_projection_sync_targets__health_status");
        ddl.Should().Contain("ux_vendor_session_projection_sync_targets__scope");
        ddl.Should().Contain("ix_vendor_session_projection_sync_targets__enabled_due");
        ddl.Should().Contain("ix_vendor_session_projection_sync_targets__health");
    }

    private static VendorSessionProjectionSyncOrchestrator CreateSut(
        IVendorSessionProjectionSyncTargetRepository repository,
        IVendorSessionProjectionSyncService syncService,
        VendorSessionProjectionOptions? options = null)
    {
        return new VendorSessionProjectionSyncOrchestrator(
            repository,
            syncService,
            new FixedClock(Now),
            Options.Create(options ?? new VendorSessionProjectionOptions
            {
                DefaultLookbackWindowMinutes = 180,
                DefaultPageSize = 100,
                MaxPagesPerRun = 5,
                MaxParallelSiteJobs = 2,
                FailingFailureCountThreshold = 3
            }),
            NullLogger<VendorSessionProjectionSyncOrchestrator>.Instance);
    }

    private static VendorSessionProjectionSyncTarget Target(string parkingLotIndexCode, bool enabled = true)
    {
        return new VendorSessionProjectionSyncTarget(
            Guid.NewGuid(),
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            parkingLotIndexCode,
            $"{parkingLotIndexCode} Name",
            enabled,
            PollIntervalSeconds: 300,
            LookbackWindowMinutes: 180,
            PageSize: 100,
            LastSuccessAt: null,
            LastFailureAt: null,
            LastAttemptAt: null,
            HealthStatus: VendorSessionProjectionHealthStatus.Unknown,
            FailureCount: 0,
            LastErrorCode: null,
            LastErrorMessage: null,
            CreatedAt: Now,
            UpdatedAt: Now);
    }

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from {AppContext.BaseDirectory}.");
    }

    private sealed class InMemoryTargetRepository(IReadOnlyList<VendorSessionProjectionSyncTarget> targets)
        : IVendorSessionProjectionSyncTargetRepository
    {
        private readonly object _gate = new();
        private readonly List<VendorSessionProjectionSyncTargetHealthUpdate> _healthUpdates = new();

        public IReadOnlyList<VendorSessionProjectionSyncTargetHealthUpdate> HealthUpdates
        {
            get
            {
                lock (_gate)
                {
                    return _healthUpdates.ToArray();
                }
            }
        }

        public Task<IReadOnlyList<VendorSessionProjectionSyncTarget>> ListDueTargetsAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<VendorSessionProjectionSyncTarget>>(
                targets.Where(target => target.Enabled).ToArray());
        }

        public Task<VendorSessionProjectionSyncTarget?> FindEnabledTargetAsync(
            Guid? siteId,
            string? parkingLotIndexCode,
            CancellationToken cancellationToken)
        {
            var found = targets
                .Where(target => target.Enabled)
                .Where(target => siteId is null || target.SiteId == siteId)
                .Where(target => parkingLotIndexCode is null || target.ParkingLotIndexCode == parkingLotIndexCode)
                .SingleOrDefault();

            return Task.FromResult(found);
        }

        public Task UpdateHealthAsync(
            VendorSessionProjectionSyncTargetHealthUpdate update,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _healthUpdates.Add(update);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSyncService : IVendorSessionProjectionSyncService
    {
        public List<SyncVendorSessionProjectionsCommand> Commands { get; } = new();

        public string? ThrowForParkingLot { get; set; }

        public Task<SyncVendorSessionProjectionsResult> SyncAsync(
            SyncVendorSessionProjectionsCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command.ParkingLotIndexCode == ThrowForParkingLot)
            {
                throw new InvalidOperationException("Synthetic target failure.");
            }

            return Task.FromResult(new SyncVendorSessionProjectionsResult(
                PagesPulled: 1,
                RecordsSeen: 3,
                RecordsProjected: 2,
                RecordsSkipped: 1,
                command.CorrelationId));
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
