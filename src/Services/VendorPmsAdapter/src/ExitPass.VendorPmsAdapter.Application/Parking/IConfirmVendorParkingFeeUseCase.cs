using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Provider-neutral use case for confirming a vendor PMS parking fee payment.
/// </summary>
public interface IConfirmVendorParkingFeeUseCase
{
    /// <summary>
    /// Confirms a vendor PMS parking fee payment.
    /// </summary>
    /// <param name="request">Provider-neutral confirmation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral confirmation response.</returns>
    Task<VendorParkingFeeConfirmationResponse> ExecuteAsync(
        VendorParkingFeeConfirmationRequest request,
        CancellationToken cancellationToken);
}
