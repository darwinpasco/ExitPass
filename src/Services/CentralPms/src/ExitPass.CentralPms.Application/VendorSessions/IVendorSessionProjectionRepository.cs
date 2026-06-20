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
