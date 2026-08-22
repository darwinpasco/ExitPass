namespace ExitPass.CentralPms.Application.VendorParking.Routing;

/// <summary>Provider-neutral non-secret route from one Site to one Site Integration Adapter.</summary>
public sealed record SiteVendorAdapterRoute(
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    Guid AdapterIdentityId,
    Uri AdapterBaseUri,
    string CredentialReference,
    string Environment,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>Resolves exactly one active adapter route for a Site and Vendor System.</summary>
public interface ISiteVendorAdapterRouteRegistry
{
    Task<SiteVendorAdapterRoute> ResolveAsync(
        Guid siteId,
        Guid siteGroupId,
        Guid? vendorSystemId,
        CancellationToken cancellationToken);
}

/// <summary>Resolves a secret from a controlled external reference without persisting it in Central PMS.</summary>
public interface ISiteAdapterCredentialResolver
{
    string Resolve(string credentialReference);
}

/// <summary>Stable fail-closed adapter routing error.</summary>
public sealed class SiteVendorAdapterRoutingException(string errorCode)
    : Exception("Site Integration Adapter routing failed safely.")
{
    public string ErrorCode { get; } = errorCode;
}
