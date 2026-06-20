using ExitPass.CentralPms.Application.VendorSessions;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// Fails projection sync explicitly when the configured Vendor PMS adapter cannot provide source records.
/// </summary>
public sealed class DisabledVendorSessionProjectionSyncService : IVendorSessionProjectionSyncService
{
    /// <inheritdoc />
    public Task<SyncVendorSessionProjectionsResult> SyncAsync(
        SyncVendorSessionProjectionsCommand command,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("VENDOR_SESSION_PROJECTION_SYNC_NOT_CONFIGURED");
    }
}
