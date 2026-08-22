namespace ExitPass.VendorPmsAdapter.Contracts.Operations;

/// <summary>Sanitized immutable identity and capability posture of one Site Integration Adapter instance.</summary>
public sealed record VendorAdapterIdentityResponse(
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    Guid AdapterIdentityId,
    string ParkingLotIndexCode,
    string Provider,
    string Environment,
    bool Activated,
    bool Ready,
    IReadOnlyList<string> Capabilities,
    string? FailureCode);
