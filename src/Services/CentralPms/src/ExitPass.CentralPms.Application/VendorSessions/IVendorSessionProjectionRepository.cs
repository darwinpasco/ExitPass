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
    /// Finds the latest known projection by card/ticket or optional plate within a scope.
    /// </summary>
    Task<VendorSessionProjection?> FindLatestAsync(
        VendorSessionProjectionLookupQuery query,
        CancellationToken cancellationToken);
}

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
