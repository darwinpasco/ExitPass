using ExitPass.VendorPmsAdapter.Contracts.Parking;

namespace ExitPass.VendorPmsAdapter.Application.Parking;

/// <summary>
/// Use case for resolving a tariff quote through the configured vendor PMS adapter.
/// </summary>
public interface IResolveVendorTariffQuoteUseCase
{
    /// <summary>
    /// Resolves a vendor tariff quote by plate number or ticket reference.
    /// </summary>
    /// <param name="request">Provider-neutral tariff quote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral tariff quote response.</returns>
    Task<VendorTariffQuoteResponse> ExecuteAsync(
        VendorTariffQuoteRequest request,
        CancellationToken cancellationToken);
}
