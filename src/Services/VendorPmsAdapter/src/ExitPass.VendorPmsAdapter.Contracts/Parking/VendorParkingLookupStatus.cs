namespace ExitPass.VendorPmsAdapter.Contracts.Parking;

/// <summary>
/// Provider-neutral outcome of a vendor PMS parking lookup.
/// </summary>
public enum VendorParkingLookupStatus
{
    /// <summary>
    /// The vendor PMS returned an active parking session or tariff quote.
    /// </summary>
    Found = 0,

    /// <summary>
    /// The vendor PMS deterministically reported that no matching session exists.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The vendor PMS was unavailable or timed out and the operation may be retried.
    /// </summary>
    UnavailableRetryable = 2,

    /// <summary>
    /// The vendor PMS returned malformed or unsupported data.
    /// </summary>
    AdapterError = 3
}
