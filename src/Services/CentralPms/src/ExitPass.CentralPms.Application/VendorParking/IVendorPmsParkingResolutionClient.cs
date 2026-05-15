using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Central PMS application boundary for resolving provider-neutral parking data from the Vendor PMS Adapter.
/// </summary>
public interface IVendorPmsParkingResolutionClient
{
    /// <summary>
    /// Resolves a provider-neutral vendor parking session by plate number or ticket reference.
    /// </summary>
    /// <param name="request">Provider-neutral vendor parking session lookup request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provider-neutral vendor parking session lookup response.</returns>
    Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a provider-neutral vendor tariff quote by plate number or ticket reference.
    /// </summary>
    /// <param name="request">Provider-neutral vendor tariff quote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provider-neutral vendor tariff quote response.</returns>
    Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken);
}
