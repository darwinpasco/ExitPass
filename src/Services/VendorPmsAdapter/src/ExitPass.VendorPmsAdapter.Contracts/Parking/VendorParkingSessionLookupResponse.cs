namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral response for a vendor parking session lookup.
/// </summary>
/// <param name="Status">Lookup status.</param>
/// <param name="Session">Resolved parking session when found.</param>
/// <param name="ErrorCode">Stable provider-neutral error code when lookup fails.</param>
/// <param name="Retryable">Whether the caller may retry the lookup.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record VendorParkingSessionLookupResponse(
    VendorParkingLookupStatus Status,
    VendorParkingSessionDto? Session,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId);
