using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Use case for resolving a parking session through the configured vendor PMS adapter.
/// </summary>
public interface IResolveVendorParkingSessionUseCase
{
    /// <summary>
    /// Resolves a parking session by plate number or ticket reference.
    /// </summary>
    /// <param name="request">Provider-neutral session lookup request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral session lookup response.</returns>
    Task<VendorParkingSessionLookupResponse> ExecuteAsync(
        VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken);
}
