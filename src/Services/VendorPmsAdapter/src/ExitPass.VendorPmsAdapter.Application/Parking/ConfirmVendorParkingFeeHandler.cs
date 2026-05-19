using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Default use case implementation for confirming vendor parking fee payments.
/// </summary>
/// <param name="client">Vendor parking data client.</param>
public sealed class ConfirmVendorParkingFeeHandler(IVendorParkingDataClient client) : IConfirmVendorParkingFeeUseCase
{
    /// <inheritdoc />
    public Task<VendorParkingFeeConfirmationResponse> ExecuteAsync(
        VendorParkingFeeConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.PlateNumber) && string.IsNullOrWhiteSpace(request.TicketReference))
        {
            throw new ArgumentException("PlateNumber or TicketReference is required.", nameof(request));
        }

        if (request.AmountMinor is null)
        {
            throw new ArgumentException("AmountMinor is required.", nameof(request));
        }

        return client.ConfirmParkingFeeAsync(request, cancellationToken);
    }
}
