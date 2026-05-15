namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral details for a confirmed vendor PMS parking fee payment.
/// </summary>
/// <param name="AmountMinor">Confirmed fee amount in minor currency units.</param>
/// <param name="Currency">ISO currency code for the confirmed fee.</param>
/// <param name="FeeTime">Vendor PMS time when the fee was charged.</param>
public sealed record VendorParkingFeeConfirmationDto(
    long AmountMinor,
    string Currency,
    DateTimeOffset FeeTime);
