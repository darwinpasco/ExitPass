namespace ExitPass.CentralPms.Contracts.Operations;

/// <summary>
/// Safe projection scheduler configuration values visible to operators.
/// </summary>
public sealed record VendorSessionProjectionHealthConfigDto(
    bool SchedulerEnabled,
    bool RequiredForEnvironment,
    bool DegradedResolveFallbackEnabled,
    int NormalFreshnessTargetSeconds,
    int MaxProjectionAgeMinutes,
    int MaxParallelSiteJobs,
    int SchedulerScanIntervalSeconds);

/// <summary>
/// Read-only target health item for vendor session projections.
/// </summary>
public sealed record VendorSessionProjectionHealthTargetDto(
    Guid ProjectionSyncTargetId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string ParkingLotIndexCode,
    string? ParkingLotName,
    bool EnabledFlag,
    string HealthStatus,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int FailureCount,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? LastLockContentionAt,
    int LockContentionCount,
    int PollIntervalSeconds,
    int LookbackWindowMinutes,
    int PageSize,
    DateTimeOffset? LatestProjectionLastRefreshedAt,
    double? FreshnessAgeSeconds,
    string FreshnessClassification,
    bool IsStale,
    long TotalProjectionCount,
    long ActiveProjectionCount,
    long ExitedProjectionCount,
    long CardNumProjectionCount,
    long PlateLicenseProjectionCount);

/// <summary>
/// Response for projection target list visibility.
/// </summary>
public sealed record VendorSessionProjectionHealthTargetsResponse(
    IReadOnlyList<VendorSessionProjectionHealthTargetDto> Targets,
    VendorSessionProjectionHealthConfigDto Config);

/// <summary>
/// Limited safe projection record visible in target detail.
/// </summary>
public sealed record VendorSessionProjectionHealthLatestRecordDto(
    Guid VendorSessionProjectionId,
    string? VendorRecordGuid,
    string? CardNum,
    string? PlateLicense,
    DateTimeOffset? EnterTime,
    DateTimeOffset? ExitTime,
    string ProjectionStatus,
    DateTimeOffset LastRefreshedAt,
    DateTimeOffset? SourceEventAt,
    Guid? CorrelationId);

/// <summary>
/// Detail response for one projection target.
/// </summary>
public sealed record VendorSessionProjectionHealthTargetDetailResponse(
    VendorSessionProjectionHealthTargetDto Target,
    IReadOnlyList<VendorSessionProjectionHealthLatestRecordDto> LatestProjectedRecords,
    VendorSessionProjectionHealthConfigDto Config);

/// <summary>
/// Dashboard summary response for projection health.
/// </summary>
public sealed record VendorSessionProjectionHealthSummaryResponse(
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
    VendorSessionProjectionHealthConfigDto Config);
