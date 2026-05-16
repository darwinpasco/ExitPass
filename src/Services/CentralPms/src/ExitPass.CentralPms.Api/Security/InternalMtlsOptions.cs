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
}
