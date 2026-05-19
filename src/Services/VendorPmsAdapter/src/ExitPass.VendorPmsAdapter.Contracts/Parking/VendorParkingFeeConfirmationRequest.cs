namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral request to confirm a vendor PMS parking fee payment.
/// </summary>
/// <param name="PlateNumber">Vehicle plate number for plate-based confirmation.</param>
/// <param name="TicketReference">Vendor ticket or card reference when plate number is unavailable.</param>
/// <param name="ImmediatelyLeave">Whether the vehicle is expected to leave immediately: 0-no, 1-yes.</param>
/// <param name="AmountMinor">Fee amount to confirm in minor currency units.</param>
/// <param name="Currency">ISO currency code for the fee amount.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record VendorParkingFeeConfirmationRequest(
    string? PlateNumber,
    string? TicketReference,
    int ImmediatelyLeave,
    long? AmountMinor,
    string Currency,
    Guid CorrelationId);
