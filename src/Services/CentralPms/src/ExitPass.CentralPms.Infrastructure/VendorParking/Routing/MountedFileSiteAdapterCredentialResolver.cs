using ExitPass.CentralPms.Application.VendorParking.Routing;

namespace ExitPass.CentralPms.Infrastructure.VendorParking.Routing;

/// <summary>Resolves only task/deployment-mounted Site Adapter service credentials.</summary>
public sealed class MountedFileSiteAdapterCredentialResolver(string secretRoot) : ISiteAdapterCredentialResolver
{
    private readonly string _root = Path.GetFullPath(
        string.IsNullOrWhiteSpace(secretRoot) ? throw new ArgumentException("Secret root is required.") : secretRoot);

    public string Resolve(string credentialReference)
    {
        if (string.IsNullOrWhiteSpace(credentialReference) ||
            !credentialReference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_CREDENTIAL_REFERENCE_INVALID");
        var relative = credentialReference[5..].TrimStart('/', '\\');
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_CREDENTIAL_UNAVAILABLE");
        var value = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_CREDENTIAL_UNAVAILABLE") : value;
    }
}
