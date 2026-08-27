namespace ExitPass.CentralPms.Api.Security;

/// <summary>
/// Options that control opt-in mTLS validation for Central PMS internal HTTP endpoints.
/// </summary>
public sealed class InternalMtlsOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether internal endpoint mTLS validation is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an internal caller must present a client certificate.
    /// </summary>
    public bool RequireClientCertificate { get; set; } = true;

    /// <summary>
    /// Gets the trusted client certificate thumbprints allowed to call internal endpoints.
    /// </summary>
    public IList<string> TrustedClientThumbprints { get; } = new List<string>();

    /// <summary>
    /// Gets deployed certificate bindings for service principals admitted to shared service routes.
    /// The binding maps verified certificate material to an existing canonical credential reference.
    /// </summary>
    public IList<InternalServicePrincipalCredentialBinding> ServicePrincipalCredentials { get; } =
        new List<InternalServicePrincipalCredentialBinding>();
}

/// <summary>
/// Deployment-owned binding between an mTLS certificate and a canonical service credential reference.
/// </summary>
public sealed class InternalServicePrincipalCredentialBinding
{
    /// <summary>Gets or sets the normalized thumbprint of the verified client certificate.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>Gets or sets the canonical credential reference stored with the service identity.</summary>
    public string CredentialReference { get; set; } = string.Empty;

    /// <summary>Gets or sets the Central PMS audience issued to the authenticated principal.</summary>
    public string Audience { get; set; } = "CENTRAL_PMS";

    /// <summary>Gets or sets the statutory source channel issued to the authenticated principal.</summary>
    public string SourceChannel { get; set; } = string.Empty;

    /// <summary>Gets the deployment-granted permissions issued to the authenticated principal.</summary>
    public IList<string> Permissions { get; } = new List<string>();
}
