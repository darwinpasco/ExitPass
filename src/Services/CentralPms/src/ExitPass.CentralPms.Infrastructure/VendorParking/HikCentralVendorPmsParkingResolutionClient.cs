using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
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
    IHikCentralLiveActivationGate activationGate,
    IVendorParkingDataClient vendorParkingDataClient) : IVendorPmsParkingResolutionClient
{
    /// <inheritdoc />
    public async Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken)
    {
        await activationGate.EnsureActivatedAsync(cancellationToken);
        return await vendorParkingDataClient.ResolveSessionAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken)
    {
        await activationGate.EnsureActivatedAsync(cancellationToken);
        return await vendorParkingDataClient.ResolveTariffAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
        VendorParkingFeeConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        await activationGate.EnsureActivatedAsync(cancellationToken);
        return await vendorParkingDataClient.ConfirmParkingFeeAsync(request, cancellationToken);
    }
}
