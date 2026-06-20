namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Lifecycle state of the latest known vendor session projection.
/// </summary>
public enum VendorSessionProjectionStatus
{
    /// <summary>
    /// Vendor record has no exit time and is latest-known active/open.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Vendor record includes an exit time.
    /// </summary>
    Exited = 1,

    /// <summary>
    /// Projection has exceeded its configured freshness window.
    /// </summary>
    Stale = 2,

    /// <summary>
    /// Projection was explicitly invalidated.
    /// </summary>
    Invalidated = 3,

    /// <summary>
    /// Source record could not be classified deterministically.
    /// </summary>
    Unknown = 4
}

/// <summary>
/// Operational health of a site-scoped vendor session projection sync target.
/// </summary>
public enum VendorSessionProjectionHealthStatus
{
    /// <summary>
    /// Target recently synchronized successfully.
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Target has projection data but has started missing expected freshness windows.
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// Target has repeated synchronization failures.
    /// </summary>
    Failing = 2,

    /// <summary>
    /// Target is configured but disabled.
    /// </summary>
    Disabled = 3,

    /// <summary>
    /// Health has not yet been established.
    /// </summary>
    Unknown = 4
}

/// <summary>
/// ExitPass-owned read model of a latest-known vendor parking session snapshot.
/// This is not vendor parking-session authority, tariff authority, payment finality, or exit authorization.
/// </summary>
public sealed record VendorSessionProjection(
    Guid VendorSessionProjectionId,
    Guid? VendorSystemId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? ParkingLotIndexCode,
    string? ParkingLotName,
    string? PassagewayIndexCode,
    string? PassagewayName,
    string? LaneIndexCode,
    string? LaneName,
    string? LaneDirection,
    string? VendorRecordGuid,
    string? CardNum,
    string? PlateLicense,
    DateTimeOffset? EnterTime,
    DateTimeOffset? ExitTime,
    string? AllowType,
    string? AllowResult,
    string? ImageUrl,
    string SourceApi,
    string SourcePayloadHash,
    string? SourcePayloadReference,
    DateTimeOffset? SourceEventAt,
    string StableIdentityType,
    string StableIdentityKey,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset LastRefreshedAt,
    VendorSessionProjectionStatus ProjectionStatus,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Site-scoped target that tells the centralized scheduler which HikCentral parking lot to project.
/// </summary>
public sealed record VendorSessionProjectionSyncTarget(
    Guid ProjectionSyncTargetId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string ParkingLotIndexCode,
    string? ParkingLotName,
    bool Enabled,
    int PollIntervalSeconds,
    int LookbackWindowMinutes,
    int PageSize,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? LastAttemptAt,
    VendorSessionProjectionHealthStatus HealthStatus,
    int FailureCount,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Command for synchronizing HikCentral passageway records into vendor session projections.
/// </summary>
public sealed record SyncVendorSessionProjectionsCommand(
    Guid? VendorSystemId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string ParkingLotIndexCode,
    DateTimeOffset BeginTime,
    DateTimeOffset EndTime,
    int PageSize,
    int MaxPages,
    Guid CorrelationId);

/// <summary>
/// Result of a projection synchronization run.
/// </summary>
public sealed record SyncVendorSessionProjectionsResult(
    int PagesPulled,
    int RecordsSeen,
    int RecordsProjected,
    int RecordsSkipped,
    Guid CorrelationId)
{
    /// <summary>
    /// Projection records upserted during the run.
    /// </summary>
    public int RecordsUpserted => RecordsProjected;
}

/// <summary>
/// Health update recorded after one site-scoped projection sync attempt.
/// </summary>
public sealed record VendorSessionProjectionSyncTargetHealthUpdate(
    Guid ProjectionSyncTargetId,
    DateTimeOffset AttemptedAt,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    int FailingFailureCountThreshold,
    Guid CorrelationId);

/// <summary>
/// Result for one site-scoped projection sync target run.
/// </summary>
public sealed record VendorSessionProjectionTargetRunResult(
    Guid ProjectionSyncTargetId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string ParkingLotIndexCode,
    bool Succeeded,
    int RecordsRead,
    int RecordsUpserted,
    int RecordsSkipped,
    int PagesPulled,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ErrorCode,
    string? ErrorMessage,
    Guid CorrelationId);

/// <summary>
/// Result for one scheduler pass across due site-scoped projection targets.
/// </summary>
public sealed record VendorSessionProjectionSchedulerRunResult(
    int TargetsLoaded,
    int TargetsRun,
    int TargetsSucceeded,
    int TargetsFailed,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<VendorSessionProjectionTargetRunResult> TargetResults);

/// <summary>
/// Manual internal command to run a scoped projection sync.
/// </summary>
public sealed record RunVendorSessionProjectionSyncCommand(
    Guid? SiteId,
    string? ParkingLotIndexCode,
    int? LookbackWindowMinutes,
    int? PageSize,
    bool Force,
    Guid CorrelationId);

/// <summary>
/// Query for a latest-known vendor session projection.
/// </summary>
public sealed record VendorSessionProjectionLookupQuery(
    string? CardNum,
    string? PlateLicense,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? ParkingLotIndexCode,
    DateTimeOffset RequestedAt,
    Guid CorrelationId);

/// <summary>
/// Snapshot lookup result that makes the authority boundary explicit.
/// </summary>
public sealed record VendorSessionProjectionLookupResult(
    bool Found,
    VendorSessionProjection? Projection,
    bool IsProjectionBased,
    bool IsAuthoritativeForParkingSession,
    bool IsAuthoritativeForTariff,
    bool IsAuthoritativeForPayment,
    TimeSpan? FreshnessAge,
    DateTimeOffset? LastRefreshedAt,
    Guid CorrelationId)
{
    /// <summary>
    /// Creates a found snapshot result.
    /// </summary>
    public static VendorSessionProjectionLookupResult FoundProjection(
        VendorSessionProjection projection,
        DateTimeOffset requestedAt,
        Guid correlationId)
    {
        return new VendorSessionProjectionLookupResult(
            Found: true,
            projection,
            IsProjectionBased: true,
            IsAuthoritativeForParkingSession: false,
            IsAuthoritativeForTariff: false,
            IsAuthoritativeForPayment: false,
            requestedAt >= projection.LastRefreshedAt ? requestedAt - projection.LastRefreshedAt : TimeSpan.Zero,
            projection.LastRefreshedAt,
            correlationId);
    }

    /// <summary>
    /// Creates a not-found snapshot result.
    /// </summary>
    public static VendorSessionProjectionLookupResult NotFound(Guid correlationId)
    {
        return new VendorSessionProjectionLookupResult(
            Found: false,
            Projection: null,
            IsProjectionBased: true,
            IsAuthoritativeForParkingSession: false,
            IsAuthoritativeForTariff: false,
            IsAuthoritativeForPayment: false,
            FreshnessAge: null,
            LastRefreshedAt: null,
            correlationId);
    }
}
