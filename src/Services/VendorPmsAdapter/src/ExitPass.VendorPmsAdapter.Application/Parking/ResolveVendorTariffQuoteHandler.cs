using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Default use case implementation for resolving vendor tariff quotes.
/// </summary>
/// <param name="client">Vendor parking data client.</param>
public sealed class ResolveVendorTariffQuoteHandler(IVendorParkingDataClient client) : IResolveVendorTariffQuoteUseCase
{
    /// <inheritdoc />
    public Task<VendorTariffQuoteResponse> ExecuteAsync(
        VendorTariffQuoteRequest request,
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

        return client.ResolveTariffAsync(request, cancellationToken);
    }
}
