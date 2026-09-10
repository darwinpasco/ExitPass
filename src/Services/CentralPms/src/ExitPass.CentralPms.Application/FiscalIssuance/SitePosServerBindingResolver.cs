namespace ExitPass.CentralPms.Application.FiscalIssuance;

/// <summary>
/// Resolves the single active POS Server binding owned by a Site.
/// </summary>
public interface ISitePosServerBindingResolver
{
    SitePosServerBindingResolution Resolve(SitePosServerBindingRequest request);
}

/// <summary>
/// Site-scoped POS Server binding lookup. Expected identity fields are optional so the
/// ordinary payment path can discover the binding while recovery paths can verify a
/// previously persisted identity without changing routes.
/// </summary>
public sealed record SitePosServerBindingRequest(
    Guid SiteId,
    Guid? ExpectedSitePosServerId = null,
    string? ExpectedSitePosServerRef = null);

public sealed record SitePosServerBindingResolution(
    bool IsSuccess,
    string Code,
    SitePosServerEndpointOptions? Endpoint)
{
    public static SitePosServerBindingResolution Success(SitePosServerEndpointOptions endpoint) =>
        new(true, SitePosServerBindingResolutionCodes.Resolved, endpoint);

    public static SitePosServerBindingResolution Failed(string code) =>
        new(false, code, null);
}

public static class SitePosServerBindingResolutionCodes
{
    public const string Resolved = "site_pos_server_site_binding_resolved";
    public const string Missing = "site_pos_server_site_binding_not_found";
    public const string Ambiguous = "site_pos_server_site_binding_ambiguous";
    public const string Inactive = "site_pos_server_site_binding_inactive";
    public const string SiteMismatch = "site_pos_server_site_binding_site_mismatch";
    public const string IdentityMissing = "site_pos_server_site_binding_identity_missing";
    public const string IdentityMismatch = "site_pos_server_site_binding_identity_mismatch";
    public const string ReferenceMissing = "site_pos_server_site_binding_reference_missing";
    public const string ReferenceMismatch = "site_pos_server_site_binding_reference_mismatch";
    public const string Inconsistent = "site_pos_server_site_binding_inconsistent";
}

/// <summary>
/// Canonical resolver over the deployment-owned Site POS Server endpoint registry.
/// Network location and credentials remain endpoint concerns and are not identity aliases.
/// </summary>
public sealed class ConfiguredSitePosServerBindingResolver : ISitePosServerBindingResolver
{
    private readonly FiscalIssuancePosServerIntegrationOptions _options;

    public ConfiguredSitePosServerBindingResolver(FiscalIssuancePosServerIntegrationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public SitePosServerBindingResolution Resolve(SitePosServerBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SiteId == Guid.Empty)
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.Missing);
        }

        var matches = _options.Endpoints
            .Where(endpoint => endpoint.SiteId == request.SiteId)
            .ToArray();

        if (matches.Length == 0)
        {
            var identityBelongsToAnotherSite = request.ExpectedSitePosServerId is { } expectedId &&
                expectedId != Guid.Empty &&
                _options.Endpoints.Any(endpoint => endpoint.SitePosServerId == expectedId);
            var referenceBelongsToAnotherSite = !string.IsNullOrWhiteSpace(request.ExpectedSitePosServerRef) &&
                _options.Endpoints.Any(endpoint => string.Equals(
                    endpoint.SitePosServerRef?.Trim(),
                    request.ExpectedSitePosServerRef.Trim(),
                    StringComparison.Ordinal));

            return SitePosServerBindingResolution.Failed(
                identityBelongsToAnotherSite || referenceBelongsToAnotherSite
                    ? SitePosServerBindingResolutionCodes.SiteMismatch
                    : SitePosServerBindingResolutionCodes.Missing);
        }

        if (matches.Length != 1)
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.Ambiguous);
        }

        var endpoint = matches[0];
        if (endpoint.SitePosServerId == Guid.Empty)
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.IdentityMissing);
        }

        if (string.IsNullOrWhiteSpace(endpoint.SitePosServerRef))
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.ReferenceMissing);
        }

        var normalizedReference = endpoint.SitePosServerRef.Trim();
        var duplicateIdentity = _options.Endpoints.Count(candidate =>
            candidate.SitePosServerId == endpoint.SitePosServerId) != 1;
        var duplicateReference = _options.Endpoints.Count(candidate => string.Equals(
            candidate.SitePosServerRef?.Trim(),
            normalizedReference,
            StringComparison.Ordinal)) != 1;
        if (duplicateIdentity || duplicateReference)
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.Inconsistent);
        }

        if (!endpoint.Enabled ||
            string.IsNullOrWhiteSpace(endpoint.Environment) ||
            !string.Equals(
                endpoint.Environment.Trim(),
                _options.RuntimeEnvironment?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.Inactive);
        }

        if (request.ExpectedSitePosServerId is { } expectedSitePosServerId &&
            (expectedSitePosServerId == Guid.Empty || endpoint.SitePosServerId != expectedSitePosServerId))
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.IdentityMismatch);
        }

        if (request.ExpectedSitePosServerRef is not null &&
            !string.Equals(
                normalizedReference,
                request.ExpectedSitePosServerRef.Trim(),
                StringComparison.Ordinal))
        {
            return SitePosServerBindingResolution.Failed(SitePosServerBindingResolutionCodes.ReferenceMismatch);
        }

        return SitePosServerBindingResolution.Success(endpoint);
    }
}
