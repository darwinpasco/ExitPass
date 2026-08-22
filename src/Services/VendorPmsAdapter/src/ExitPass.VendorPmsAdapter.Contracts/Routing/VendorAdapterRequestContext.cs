namespace ExitPass.VendorPmsAdapter.Contracts.Routing;

/// <summary>
/// Immutable ExitPass routing scope carried on every provider-neutral Site Integration Adapter request.
/// </summary>
/// <remarks>
/// ExitPass v1.3 connector design Sections 8-10 require Site, Site Group, Vendor System, and connector
/// instance identities to remain distinct and fail closed on mismatch. The adapter cannot grant payment,
/// discount, fiscal, exit, or physical-gate authority.
/// </remarks>
public sealed record VendorAdapterRequestContext(
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    Guid AdapterIdentityId);

/// <summary>
/// Non-secret adapter identity returned with a provider-neutral response.
/// </summary>
public sealed record VendorAdapterResponseContext(
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    Guid AdapterIdentityId,
    string ParkingLotIndexCode,
    string Environment);
