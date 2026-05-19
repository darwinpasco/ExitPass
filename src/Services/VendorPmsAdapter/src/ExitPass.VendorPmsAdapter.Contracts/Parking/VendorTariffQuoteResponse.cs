namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral response for a vendor PMS tariff quote lookup.
/// </summary>
/// <param name="Status">Lookup status.</param>
/// <param name="Quote">Resolved tariff quote when found.</param>
/// <param name="ErrorCode">Stable provider-neutral error code when lookup fails.</param>
/// <param name="Retryable">Whether the caller may retry the lookup.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record VendorTariffQuoteResponse(
    VendorParkingLookupStatus Status,
    VendorTariffQuoteDto? Quote,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId);
