using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.CentralPms.Infrastructure.VendorParking;

/// <summary>
/// Central PMS provider-neutral client that delegates session and tariff lookup to the HikCentral adapter.
/// </summary>
/// <param name="vendorParkingDataClient">HikCentral-backed Vendor PMS Adapter client.</param>
/// <remarks>
/// BRD v1.2: validates vendor PMS parking lookup and tariff calculation through a configured adapter.
/// SDD v1.2: Central PMS consumes canonical Vendor PMS Adapter contracts, not HikCentral-specific DTOs.
/// Invariant: HikCentral lookup/tariff evidence is not payment finality and cannot issue exit authorization.
/// </remarks>
public sealed class HikCentralVendorPmsParkingResolutionClient(
    IVendorParkingDataClient vendorParkingDataClient) : IVendorPmsParkingResolutionClient
{
    /// <inheritdoc />
    public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken)
    {
        return vendorParkingDataClient.ResolveSessionAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken)
    {
        return vendorParkingDataClient.ResolveTariffAsync(request, cancellationToken);
    }
}
