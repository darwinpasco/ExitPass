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
