using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Safe projection scheduler configuration values that operators may view.
/// </summary>
public sealed record VendorSessionProjectionHealthConfig(
    bool SchedulerEnabled,
    bool DegradedResolveFallbackEnabled,
    int MaxProjectionAgeMinutes,
    int MaxParallelSiteJobs,
    int SchedulerScanIntervalSeconds);

/// <summary>
/// Read-only health view for one vendor session projection sync target.
/// </summary>
public sealed record VendorSessionProjectionHealthTarget(
    Guid ProjectionSyncTargetId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string ParkingLotIndexCode,
    string? ParkingLotName,
    bool Enabled,
    VendorSessionProjectionHealthStatus HealthStatus,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int FailureCount,
    string? LastErrorCode,
    string? LastErrorMessage,
    int PollIntervalSeconds,
    int LookbackWindowMinutes,
    int PageSize,
    DateTimeOffset? LatestProjectionLastRefreshedAt,
    TimeSpan? FreshnessAge,
    bool IsStale,
    long TotalProjectionCount,
    long ActiveProjectionCount,
    long ExitedProjectionCount,
    long CardNumProjectionCount,
    long PlateLicenseProjectionCount);

/// <summary>
/// Limited, safe projection record shown on target detail pages.
/// </summary>
public sealed record VendorSessionProjectionHealthLatestRecord(
    Guid VendorSessionProjectionId,
    string? VendorRecordGuid,
    string? CardNum,
    string? PlateLicense,
    DateTimeOffset? EnterTime,
    DateTimeOffset? ExitTime,
    VendorSessionProjectionStatus ProjectionStatus,
    DateTimeOffset LastRefreshedAt,
    DateTimeOffset? SourceEventAt,
    Guid? CorrelationId);

/// <summary>
/// Detail health view for one projection sync target.
/// </summary>
public sealed record VendorSessionProjectionHealthDetail(
    VendorSessionProjectionHealthTarget Target,
    IReadOnlyList<VendorSessionProjectionHealthLatestRecord> LatestProjectedRecords);

/// <summary>
/// Aggregate dashboard summary for vendor session projection health.
/// </summary>
public sealed record VendorSessionProjectionHealthSummary(
    int TotalTargets,
    int EnabledTargets,
    int DisabledTargets,
    int HealthyTargets,
    int DegradedTargets,
    int FailingTargets,
    int UnknownTargets,
    int StaleTargets,
    int TargetsWithLastFailure,
    DateTimeOffset? LatestSuccessfulProjectionSyncAt,
    long TotalActiveProjections,
    long TotalExitedProjections,
    VendorSessionProjectionHealthConfig Config);

/// <summary>
/// Read repository for operator-facing projection health data.
/// </summary>
public interface IVendorSessionProjectionHealthReadRepository
{
    /// <summary>
    /// Lists all configured projection sync targets with projection rollups.
    /// </summary>
    Task<IReadOnlyList<VendorSessionProjectionHealthTargetReadModel>> ListTargetsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one projection sync target with projection rollups.
    /// </summary>
    Task<VendorSessionProjectionHealthTargetReadModel?> GetTargetAsync(
        Guid projectionSyncTargetId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the latest safe projection records for one target.
    /// </summary>
    Task<IReadOnlyList<VendorSessionProjectionHealthLatestRecord>> ListLatestRecordsAsync(
        Guid projectionSyncTargetId,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Raw target health read model before freshness calculations are applied.
/// </summary>
public sealed record VendorSessionProjectionHealthTargetReadModel(
    Guid ProjectionSyncTargetId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string ParkingLotIndexCode,
    string? ParkingLotName,
    bool Enabled,
    VendorSessionProjectionHealthStatus HealthStatus,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int FailureCount,
    string? LastErrorCode,
    string? LastErrorMessage,
    int PollIntervalSeconds,
    int LookbackWindowMinutes,
    int PageSize,
    DateTimeOffset? LatestProjectionLastRefreshedAt,
    long TotalProjectionCount,
    long ActiveProjectionCount,
    long ExitedProjectionCount,
    long CardNumProjectionCount,
    long PlateLicenseProjectionCount);

/// <summary>
/// Read-only service for operator projection health visibility.
/// </summary>
public interface IVendorSessionProjectionHealthService
{
    /// <summary>
    /// Lists projection sync target health.
    /// </summary>
    Task<IReadOnlyList<VendorSessionProjectionHealthTarget>> ListTargetsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets one projection sync target health detail.
    /// </summary>
    Task<VendorSessionProjectionHealthDetail?> GetTargetAsync(
        Guid projectionSyncTargetId,
        int latestRecordLimit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets aggregate projection health summary.
    /// </summary>
    Task<VendorSessionProjectionHealthSummary> GetSummaryAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Application service that computes freshness and summary metrics for projection health.
/// </summary>
public sealed class VendorSessionProjectionHealthService(
    IVendorSessionProjectionHealthReadRepository repository,
    ISystemClock clock,
    Microsoft.Extensions.Options.IOptions<VendorSessionProjectionOptions> options)
    : IVendorSessionProjectionHealthService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VendorSessionProjectionHealthTarget>> ListTargetsAsync(
        CancellationToken cancellationToken)
    {
        var targets = await repository.ListTargetsAsync(cancellationToken);
        return targets.Select(ToTarget).ToArray();
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjectionHealthDetail?> GetTargetAsync(
        Guid projectionSyncTargetId,
        int latestRecordLimit,
        CancellationToken cancellationToken)
    {
        var target = await repository.GetTargetAsync(projectionSyncTargetId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var latestRecords = await repository.ListLatestRecordsAsync(
            projectionSyncTargetId,
            Math.Clamp(latestRecordLimit, 1, 100),
            cancellationToken);

        return new VendorSessionProjectionHealthDetail(ToTarget(target), latestRecords);
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjectionHealthSummary> GetSummaryAsync(
        CancellationToken cancellationToken)
    {
        var targets = (await ListTargetsAsync(cancellationToken)).ToArray();
        return new VendorSessionProjectionHealthSummary(
            TotalTargets: targets.Length,
            EnabledTargets: targets.Count(target => target.Enabled),
            DisabledTargets: targets.Count(target => !target.Enabled),
            HealthyTargets: targets.Count(target => target.HealthStatus == VendorSessionProjectionHealthStatus.Healthy),
            DegradedTargets: targets.Count(target => target.HealthStatus == VendorSessionProjectionHealthStatus.Degraded),
            FailingTargets: targets.Count(target => target.HealthStatus == VendorSessionProjectionHealthStatus.Failing),
            UnknownTargets: targets.Count(target => target.HealthStatus == VendorSessionProjectionHealthStatus.Unknown),
            StaleTargets: targets.Count(target => target.IsStale),
            TargetsWithLastFailure: targets.Count(target => target.LastFailureAt is not null),
            LatestSuccessfulProjectionSyncAt: targets
                .Where(target => target.LastSuccessAt is not null)
                .Select(target => target.LastSuccessAt)
                .Max(),
            TotalActiveProjections: targets.Sum(target => target.ActiveProjectionCount),
            TotalExitedProjections: targets.Sum(target => target.ExitedProjectionCount),
            BuildConfig());
    }

    private VendorSessionProjectionHealthTarget ToTarget(VendorSessionProjectionHealthTargetReadModel readModel)
    {
        var now = clock.UtcNow;
        var freshnessAge = readModel.LatestProjectionLastRefreshedAt.HasValue
            ? (now >= readModel.LatestProjectionLastRefreshedAt.Value
                ? now - readModel.LatestProjectionLastRefreshedAt.Value
                : TimeSpan.Zero)
            : (TimeSpan?)null;
        var maxAge = options.Value.EffectiveMaxProjectionAge();
        var isStale = readModel.Enabled &&
            (freshnessAge is null || freshnessAge > maxAge);

        return new VendorSessionProjectionHealthTarget(
            readModel.ProjectionSyncTargetId,
            readModel.SiteId,
            readModel.SiteGroupId,
            readModel.VendorSystemId,
            readModel.ParkingLotIndexCode,
            readModel.ParkingLotName,
            readModel.Enabled,
            readModel.HealthStatus,
            readModel.LastAttemptAt,
            readModel.LastSuccessAt,
            readModel.LastFailureAt,
            readModel.FailureCount,
            readModel.LastErrorCode,
            readModel.LastErrorMessage,
            readModel.PollIntervalSeconds,
            readModel.LookbackWindowMinutes,
            readModel.PageSize,
            readModel.LatestProjectionLastRefreshedAt,
            freshnessAge,
            isStale,
            readModel.TotalProjectionCount,
            readModel.ActiveProjectionCount,
            readModel.ExitedProjectionCount,
            readModel.CardNumProjectionCount,
            readModel.PlateLicenseProjectionCount);
    }

    private VendorSessionProjectionHealthConfig BuildConfig()
    {
        var value = options.Value;
        return new VendorSessionProjectionHealthConfig(
            value.SchedulerEnabled,
            value.DegradedResolveFallbackEnabled,
            value.MaxProjectionAgeMinutes,
            value.MaxParallelSiteJobs,
            value.SchedulerScanIntervalSeconds);
    }
}
