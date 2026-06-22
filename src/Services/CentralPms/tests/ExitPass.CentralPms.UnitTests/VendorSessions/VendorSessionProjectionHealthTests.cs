using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.VendorSessions;

public sealed class VendorSessionProjectionHealthTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-22T08:00:00Z");

    [Fact]
    public async Task ListTargetsAsync_ReturnsReadOnlyHealthDataAndStaleStatus()
    {
        var repository = new RecordingHealthRepository(
            [
                Target(
                    "LOT-1",
                    enabled: true,
                    healthStatus: VendorSessionProjectionHealthStatus.Healthy,
                    latestProjectionLastRefreshedAt: Now.AddMinutes(-30),
                    activeCount: 3,
                    exitedCount: 1,
                    cardCount: 2,
                    plateCount: 1),
                Target(
                    "LOT-2",
                    enabled: true,
                    healthStatus: VendorSessionProjectionHealthStatus.Degraded,
                    latestProjectionLastRefreshedAt: Now.AddMinutes(-90)),
                Target(
                    "LOT-3",
                    enabled: false,
                    healthStatus: VendorSessionProjectionHealthStatus.Disabled,
                    latestProjectionLastRefreshedAt: Now.AddDays(-10))
            ]);
        var sut = CreateSut(repository);

        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        targets.Should().HaveCount(3);
        targets.Single(target => target.ParkingLotIndexCode == "LOT-1").Should().Match<VendorSessionProjectionHealthTarget>(
            target => !target.IsStale &&
                target.ActiveProjectionCount == 3 &&
                target.ExitedProjectionCount == 1 &&
                target.CardNumProjectionCount == 2 &&
                target.PlateLicenseProjectionCount == 1 &&
                target.FreshnessAge == TimeSpan.FromMinutes(30));
        targets.Single(target => target.ParkingLotIndexCode == "LOT-2").IsStale.Should().BeTrue();
        targets.Single(target => target.ParkingLotIndexCode == "LOT-3").Should().Match<VendorSessionProjectionHealthTarget>(
            target => !target.Enabled &&
                target.HealthStatus == VendorSessionProjectionHealthStatus.Disabled &&
                !target.IsStale);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsTargetsHealthAndProjectionStatus()
    {
        var repository = new RecordingHealthRepository(
            [
                Target("HEALTHY", true, VendorSessionProjectionHealthStatus.Healthy, Now.AddMinutes(-10), activeCount: 2, exitedCount: 1, lastSuccessAt: Now.AddMinutes(-9)),
                Target("DEGRADED", true, VendorSessionProjectionHealthStatus.Degraded, Now.AddHours(-2), activeCount: 4, exitedCount: 2, lastFailureAt: Now.AddMinutes(-5)),
                Target("FAILING", true, VendorSessionProjectionHealthStatus.Failing, null),
                Target("UNKNOWN", true, VendorSessionProjectionHealthStatus.Unknown, Now.AddMinutes(-15)),
                Target("DISABLED", false, VendorSessionProjectionHealthStatus.Disabled, Now.AddDays(-2))
            ]);
        var sut = CreateSut(repository);

        var summary = await sut.GetSummaryAsync(CancellationToken.None);

        summary.TotalTargets.Should().Be(5);
        summary.EnabledTargets.Should().Be(4);
        summary.DisabledTargets.Should().Be(1);
        summary.HealthyTargets.Should().Be(1);
        summary.DegradedTargets.Should().Be(1);
        summary.FailingTargets.Should().Be(1);
        summary.UnknownTargets.Should().Be(1);
        summary.StaleTargets.Should().Be(2);
        summary.TargetsWithLastFailure.Should().Be(1);
        summary.LatestSuccessfulProjectionSyncAt.Should().Be(Now.AddMinutes(-9));
        summary.TotalActiveProjections.Should().Be(6);
        summary.TotalExitedProjections.Should().Be(3);
    }

    [Fact]
    public async Task GetTargetAsync_ReturnsTargetDetailAndLatestProjectionRows()
    {
        var target = Target("LOT-DETAIL", true, VendorSessionProjectionHealthStatus.Healthy, Now.AddMinutes(-5));
        var latest = new VendorSessionProjectionHealthLatestRecord(
            Guid.Parse("99999999-0000-0000-0000-000000000001"),
            "GUID-1",
            "3519278781100",
            null,
            Now.AddHours(-1),
            null,
            VendorSessionProjectionStatus.Active,
            Now.AddMinutes(-5),
            Now.AddHours(-1),
            Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"));
        var repository = new RecordingHealthRepository([target], [latest]);
        var sut = CreateSut(repository);

        var detail = await sut.GetTargetAsync(target.ProjectionSyncTargetId, 20, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Target.ParkingLotIndexCode.Should().Be("LOT-DETAIL");
        detail.LatestProjectedRecords.Should().ContainSingle()
            .Which.CardNum.Should().Be("3519278781100");
    }

    [Fact]
    public async Task GetSummaryAsync_ExposesOnlySafeConfigurationFlags()
    {
        var sut = CreateSut(
            new RecordingHealthRepository([]),
            new VendorSessionProjectionOptions
            {
                SchedulerEnabled = true,
                DegradedResolveFallbackEnabled = false,
                MaxProjectionAgeMinutes = 45,
                MaxParallelSiteJobs = 3,
                SchedulerScanIntervalSeconds = 60
            });

        var summary = await sut.GetSummaryAsync(CancellationToken.None);

        summary.Config.SchedulerEnabled.Should().BeTrue();
        summary.Config.DegradedResolveFallbackEnabled.Should().BeFalse();
        summary.Config.MaxProjectionAgeMinutes.Should().Be(45);
        summary.Config.MaxParallelSiteJobs.Should().Be(3);
        summary.Config.SchedulerScanIntervalSeconds.Should().Be(60);
        summary.Config.GetType().GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(propertyName =>
                propertyName.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("AppKey", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Contains("BaseUrl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CentralPmsRbacPolicyCatalog_MapsProjectionHealthViewerToReadPermissions()
    {
        var permissions = CentralPmsRbacPolicyCatalog.ResolvePermissions("VendorSessionProjectionHealthViewer");

        permissions.Should().Contain("ops.vendor-session-projection-health.view");
        permissions.Should().Contain("operator-console.vendor-projection-health.view");
    }

    [Fact]
    public void VendorSessionProjectionHealthEndpoints_DoNotDefineMutationRoutes()
    {
        var endpointFile = File.ReadAllText(FindRepoFile("VendorSessionProjectionHealthEndpoints.cs"));

        endpointFile.Should().Contain("MapGet");
        endpointFile.Should().NotContain("MapPost");
        endpointFile.Should().NotContain("MapPut");
        endpointFile.Should().NotContain("MapPatch");
        endpointFile.Should().NotContain("MapDelete");
        endpointFile.Should().Contain("ReconciliationPolicyMetadata(ViewerPolicy)");
    }

    private static VendorSessionProjectionHealthService CreateSut(
        IVendorSessionProjectionHealthReadRepository repository,
        VendorSessionProjectionOptions? options = null)
    {
        return new VendorSessionProjectionHealthService(
            repository,
            new FixedClock(Now),
            Options.Create(options ?? new VendorSessionProjectionOptions
            {
                MaxProjectionAgeMinutes = 60,
                SchedulerScanIntervalSeconds = 30,
                MaxParallelSiteJobs = 2
            }));
    }

    private static VendorSessionProjectionHealthTargetReadModel Target(
        string parkingLotIndexCode,
        bool enabled,
        VendorSessionProjectionHealthStatus healthStatus,
        DateTimeOffset? latestProjectionLastRefreshedAt,
        long activeCount = 0,
        long exitedCount = 0,
        long cardCount = 0,
        long plateCount = 0,
        DateTimeOffset? lastSuccessAt = null,
        DateTimeOffset? lastFailureAt = null)
    {
        return new VendorSessionProjectionHealthTargetReadModel(
            Guid.NewGuid(),
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            parkingLotIndexCode,
            $"{parkingLotIndexCode} Name",
            enabled,
            healthStatus,
            LastAttemptAt: lastSuccessAt ?? lastFailureAt,
            LastSuccessAt: lastSuccessAt,
            LastFailureAt: lastFailureAt,
            FailureCount: lastFailureAt.HasValue ? 1 : 0,
            LastErrorCode: lastFailureAt.HasValue ? "SYNTHETIC_FAILURE" : null,
            LastErrorMessage: lastFailureAt.HasValue ? "Synthetic failure." : null,
            PollIntervalSeconds: 300,
            LookbackWindowMinutes: 180,
            PageSize: 100,
            latestProjectionLastRefreshedAt,
            TotalProjectionCount: activeCount + exitedCount,
            ActiveProjectionCount: activeCount,
            ExitedProjectionCount: exitedCount,
            CardNumProjectionCount: cardCount,
            PlateLicenseProjectionCount: plateCount);
    }

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Directory
                .EnumerateFiles(directory.FullName, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (candidate is not null)
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from {AppContext.BaseDirectory}.");
    }

    private sealed class RecordingHealthRepository(
        IReadOnlyList<VendorSessionProjectionHealthTargetReadModel> targets,
        IReadOnlyList<VendorSessionProjectionHealthLatestRecord>? latestRecords = null)
        : IVendorSessionProjectionHealthReadRepository
    {
        public Task<IReadOnlyList<VendorSessionProjectionHealthTargetReadModel>> ListTargetsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(targets);

        public Task<VendorSessionProjectionHealthTargetReadModel?> GetTargetAsync(
            Guid projectionSyncTargetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(targets.SingleOrDefault(target => target.ProjectionSyncTargetId == projectionSyncTargetId));

        public Task<IReadOnlyList<VendorSessionProjectionHealthLatestRecord>> ListLatestRecordsAsync(
            Guid projectionSyncTargetId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VendorSessionProjectionHealthLatestRecord>>(
                (latestRecords ?? []).Take(limit).ToArray());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
