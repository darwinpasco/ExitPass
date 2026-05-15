namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>
/// Central PMS outcome for provider-neutral vendor parking resolution.
/// </summary>
public enum ResolveVendorParkingOutcome
{
    /// <summary>
    /// Vendor session and tariff data were resolved and mapped into Central PMS domain objects.
    /// </summary>
    Resolved = 0,

    /// <summary>
    /// Vendor PMS deterministically reported that no matching session exists.
    /// </summary>
    SessionNotFound = 1,

    /// <summary>
    /// Vendor PMS or the adapter was unavailable and the lookup may be retried.
    /// </summary>
    RetryableUnavailable = 2,

    /// <summary>
    /// Vendor PMS Adapter returned malformed or unsupported provider-neutral data.
    /// </summary>
    MalformedVendorResponse = 3,

    /// <summary>
    /// Central PMS rejected the lookup request before calling the Vendor PMS Adapter.
    /// </summary>
    InvalidRequest = 4,

    /// <summary>
    /// Vendor PMS rejected the lookup with a non-success business response.
    /// </summary>
    VendorRejected = 5
}
