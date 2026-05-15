namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral response for a vendor PMS parking fee confirmation.
/// </summary>
/// <param name="Status">Confirmation status.</param>
/// <param name="Confirmation">Confirmed fee details when the vendor PMS accepts the confirmation.</param>
/// <param name="ErrorCode">Stable provider-neutral error code when confirmation fails.</param>
/// <param name="Retryable">Whether the caller may retry the confirmation.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record VendorParkingFeeConfirmationResponse(
    VendorParkingLookupStatus Status,
    VendorParkingFeeConfirmationDto? Confirmation,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId);
