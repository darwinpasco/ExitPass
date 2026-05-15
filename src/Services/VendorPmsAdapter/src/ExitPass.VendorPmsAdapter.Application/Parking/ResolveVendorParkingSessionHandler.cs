using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Default use case implementation for resolving vendor parking sessions.
/// </summary>
/// <param name="client">Vendor parking data client.</param>
public sealed class ResolveVendorParkingSessionHandler(IVendorParkingDataClient client) : IResolveVendorParkingSessionUseCase
{
    /// <inheritdoc />
    public Task<VendorParkingSessionLookupResponse> ExecuteAsync(
        VendorParkingSessionLookupRequest request,
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

        return client.ResolveSessionAsync(request, cancellationToken);
    }
}
