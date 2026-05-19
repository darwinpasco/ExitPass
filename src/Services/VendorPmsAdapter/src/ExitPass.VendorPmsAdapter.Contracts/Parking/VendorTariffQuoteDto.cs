namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral representation of a vendor PMS tariff quote.
/// </summary>
/// <param name="AmountMinor">Quoted fee in minor currency units.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="TariffVersionReference">Vendor tariff rule or version reference.</param>
/// <param name="TariffName">Vendor tariff rule display name.</param>
/// <param name="CalculatedAt">Time the adapter calculated or received the quote.</param>
public sealed record VendorTariffQuoteDto(
    long AmountMinor,
    string Currency,
    string? TariffVersionReference,
    string? TariffName,
    DateTimeOffset CalculatedAt);
