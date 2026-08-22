using ExitPass.VendorPmsAdapter.Contracts.Routing;

namespace ExitPass.VendorPmsAdapter.Application.Routing;

/// <summary>Immutable one-Site binding enforced by one Site Integration Adapter process.</summary>
public sealed record SiteAdapterBinding(
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    Guid AdapterIdentityId,
    Guid AllowedCentralPmsServiceIdentityId,
    string ParkingLotIndexCode,
    string Environment,
    bool Activated)
{
    /// <summary>Builds the non-secret response context for the bound adapter.</summary>
    public VendorAdapterResponseContext ToResponseContext() => new(
        SiteId,
        SiteGroupId,
        VendorSystemId,
        AdapterIdentityId,
        ParkingLotIndexCode,
        Environment);
}

/// <summary>Rejects cross-Site, cross-Vendor, or wrong-adapter requests before provider I/O.</summary>
public sealed class SiteAdapterBindingGuard(SiteAdapterBinding binding)
{
    /// <summary>Validates one provider-neutral request against the immutable process binding.</summary>
    public void EnsureCompatible(VendorAdapterRequestContext? context, string? parkingLotIndexCode = null)
    {
        if (!binding.Activated)
        {
            throw new SiteAdapterBindingException("SITE_ADAPTER_DISABLED");
        }

        if (context is null ||
            context.SiteId != binding.SiteId ||
            context.SiteGroupId != binding.SiteGroupId ||
            context.VendorSystemId != binding.VendorSystemId ||
            context.AdapterIdentityId != binding.AdapterIdentityId)
        {
            throw new SiteAdapterBindingException("SITE_ADAPTER_BINDING_MISMATCH");
        }

        if (parkingLotIndexCode is not null &&
            !string.Equals(parkingLotIndexCode.Trim(), binding.ParkingLotIndexCode, StringComparison.Ordinal))
        {
            throw new SiteAdapterBindingException("SITE_ADAPTER_PARKING_LOT_MISMATCH");
        }
    }
}

/// <summary>Stable, sanitized binding rejection raised before any HikCentral call.</summary>
public sealed class SiteAdapterBindingException(string errorCode)
    : Exception("Site Integration Adapter request was rejected safely.")
{
    public string ErrorCode { get; } = errorCode;
}
