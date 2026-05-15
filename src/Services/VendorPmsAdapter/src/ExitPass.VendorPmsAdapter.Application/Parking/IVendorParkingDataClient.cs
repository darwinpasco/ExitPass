using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Provider-neutral client boundary for resolving parking data from a vendor PMS.
/// </summary>
public interface IVendorParkingDataClient
{
    /// <summary>
    /// Resolves a parking session from the vendor PMS.
    /// </summary>
    /// <param name="request">Provider-neutral session lookup request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral session lookup response.</returns>
    Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a tariff quote from the vendor PMS.
    /// </summary>
    /// <param name="request">Provider-neutral tariff quote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral tariff quote response.</returns>
    Task<VendorTariffQuoteResponse> ResolveTariffAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a vendor PMS parking fee payment.
    /// </summary>
    /// <param name="request">Provider-neutral confirmation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral confirmation response.</returns>
    Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
        VendorParkingFeeConfirmationRequest request,
        CancellationToken cancellationToken);
}
