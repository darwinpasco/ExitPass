namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Persistence boundary for ExitPass-owned vendor session projection snapshots.
/// </summary>
public interface IVendorSessionProjectionRepository
{
    /// <summary>
    /// Creates or updates a projection using the stable vendor identity key.
    /// </summary>
    Task<VendorSessionProjection> UpsertAsync(
        VendorSessionProjection projection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates or updates a complete successful projection batch.
    /// </summary>
    Task<IReadOnlyList<VendorSessionProjection>> UpsertBatchAsync(
        IReadOnlyList<VendorSessionProjection> projections,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the latest known projection by card/ticket or optional plate within a scope.
    /// </summary>
    Task<VendorSessionProjectionReadResult?> FindLatestAsync(
        VendorSessionProjectionLookupQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Projection row plus the authoritative timestamp of its target's last completed successful cycle.
/// </summary>
public sealed record VendorSessionProjectionReadResult(
    VendorSessionProjection Projection,
    DateTimeOffset? LastSuccessfulProjectionAt);

/// <summary>
/// Persistence boundary for site-scoped vendor session projection sync targets.
/// </summary>
public interface IVendorSessionProjectionSyncTargetRepository
{
    /// <summary>
    /// Lists enabled targets that are due for a scheduler pass.
    /// </summary>
    Task<IReadOnlyList<VendorSessionProjectionSyncTarget>> ListDueTargetsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds one enabled target by site or parking lot scope for a manual trigger.
    /// </summary>
    Task<VendorSessionProjectionSyncTarget?> FindEnabledTargetAsync(
        Guid? siteId,
        string? parkingLotIndexCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records health and freshness after a sync attempt.
    /// </summary>
    Task UpdateHealthAsync(
        VendorSessionProjectionSyncTargetHealthUpdate update,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a deferred cycle caused by target-scoped distributed lock contention.
    /// </summary>
    Task RecordLockContentionAsync(
        Guid projectionSyncTargetId,
        DateTimeOffset contendedAt,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Target-scoped cross-instance projection execution lock.
/// </summary>
public interface IVendorSessionProjectionExecutionLock
{
    /// <summary>
    /// Attempts to acquire an exclusive lease for one projection target.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(
        Guid projectionSyncTargetId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fail-closed activation boundary shared by every live HikCentral adapter path.
/// </summary>
public interface IHikCentralLiveActivationGate
{
    /// <summary>
    /// Verifies the dedicated local profile, process configuration, acknowledgement, and target scope.
    /// </summary>
    Task EnsureActivatedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read service for continuity/degraded-mode vendor session projection lookup.
/// </summary>
public interface IVendorSessionProjectionLookupService
{
    /// <summary>
    /// Looks up the latest known vendor session projection without claiming vendor authority.
    /// </summary>
    Task<VendorSessionProjectionLookupResult> LookupAsync(
        VendorSessionProjectionLookupQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// On-demand synchronization service for vendor session projections.
/// </summary>
public interface IVendorSessionProjectionSyncService
{
    /// <summary>
    /// Pulls vendor passageway records and persists projection snapshots.
    /// </summary>
    Task<SyncVendorSessionProjectionsResult> SyncAsync(
        SyncVendorSessionProjectionsCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Centralized orchestrator for scheduled and manual site-scoped projection sync runs.
/// </summary>
public interface IVendorSessionProjectionSyncOrchestrator
{
    /// <summary>
    /// Runs one scheduler pass for all due enabled targets.
    /// </summary>
    Task<VendorSessionProjectionSchedulerRunResult> RunDueTargetsOnceAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a manual sync for exactly one site or parking-lot target.
    /// </summary>
    Task<VendorSessionProjectionTargetRunResult> RunManualAsync(
        RunVendorSessionProjectionSyncCommand command,
        CancellationToken cancellationToken);
}
