namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral request for resolving a vendor PMS tariff quote.
/// </summary>
/// <param name="PlateNumber">Vehicle plate number to query.</param>
/// <param name="TicketReference">Vendor ticket reference to query when plate number is unavailable.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record VendorTariffQuoteRequest(
    string? PlateNumber,
    string? TicketReference,
    Guid CorrelationId);
