namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral representation of a vendor parking session.
/// </summary>
/// <param name="VendorProviderCode">Vendor provider code, for example HIKCENTRAL.</param>
/// <param name="VendorSessionReference">Vendor session reference stable enough for audit and downstream correlation.</param>
/// <param name="PlateNumber">Vehicle plate number returned by the vendor PMS.</param>
/// <param name="EntryTime">Vehicle parking entry time.</param>
/// <param name="ParkingDurationSeconds">Parking duration in seconds when supplied by the vendor PMS.</param>
/// <param name="Status">Provider-neutral session status.</param>
/// <param name="TariffQuote">Current vendor tariff quote when returned with the session.</param>
public sealed record VendorParkingSessionDto(
    string VendorProviderCode,
    string VendorSessionReference,
    string PlateNumber,
    DateTimeOffset EntryTime,
    int? ParkingDurationSeconds,
    string Status,
    VendorTariffQuoteDto? TariffQuote);
