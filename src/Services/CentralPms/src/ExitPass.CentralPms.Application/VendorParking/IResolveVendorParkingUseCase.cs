namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Central PMS use case for resolving provider-neutral vendor parking data.
/// </summary>
public interface IResolveVendorParkingUseCase
{
    /// <summary>
    /// Resolves vendor parking session and tariff data by plate number or ticket reference.
    /// </summary>
    /// <param name="command">Vendor parking resolution command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider-neutral vendor parking resolution result mapped to Central PMS objects.</returns>
    Task<ResolveVendorParkingResult> ExecuteAsync(
        ResolveVendorParkingCommand command,
        CancellationToken cancellationToken);
}
