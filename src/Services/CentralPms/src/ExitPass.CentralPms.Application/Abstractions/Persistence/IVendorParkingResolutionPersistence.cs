namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Persists provider-neutral vendor parking resolution data into Central PMS canonical storage.
/// </summary>
public interface IVendorParkingResolutionPersistence
{
    /// <summary>
    /// Persists or reuses the canonical Central PMS parking session and tariff snapshot for a vendor resolution.
    /// </summary>
    /// <param name="request">Vendor parking persistence request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted or reused Central PMS parking session and tariff snapshot.</returns>
    Task<PersistVendorParkingResolutionResult> PersistAsync(
        PersistVendorParkingResolutionRequest request,
        CancellationToken cancellationToken);
}
