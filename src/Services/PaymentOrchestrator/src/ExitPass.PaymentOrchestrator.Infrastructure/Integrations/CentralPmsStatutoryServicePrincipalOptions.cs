using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Integrations;

/// <summary>
/// Transport configuration for the WebPay statutory service-principal boundary.
/// </summary>
public sealed class CentralPmsStatutoryServicePrincipalOptions
{
    /// <summary>Configuration section containing the statutory transport settings.</summary>
    public const string SectionName = "Integrations:CentralPms:StatutoryDiscounts";

    /// <summary>Gets or sets whether Central PMS must derive statutory authority from mTLS.</summary>
    public bool UseServerDerivedServicePrincipal { get; set; }

    /// <summary>Gets or sets the HTTPS Central PMS base URL used only for statutory operations.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the external PKCS#12 client certificate path.</summary>
    public string ClientCertificatePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the external file containing the PKCS#12 password.</summary>
    public string ClientCertificatePasswordFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the external trusted Central PMS root certificate path.</summary>
    public string TrustedServerCertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Validates deployment-owned transport material when service-principal mode is enabled.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        if (!UseServerDerivedServicePrincipal)
        {
            return [];
        }

        var errors = new List<string>();
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{SectionName}:BaseUrl must be an absolute HTTPS URL when server-derived service-principal mode is enabled.");
        }

        ValidateFile(ClientCertificatePath, nameof(ClientCertificatePath), errors);
        ValidateFile(ClientCertificatePasswordFile, nameof(ClientCertificatePasswordFile), errors);
        ValidateFile(TrustedServerCertificatePath, nameof(TrustedServerCertificatePath), errors);

        if (errors.Count == 0)
        {
            try
            {
                using var certificate = LoadClientCertificate(this);
                if (!certificate.HasPrivateKey)
                {
                    errors.Add($"{SectionName}:ClientCertificatePath must contain a private key.");
                }

                if (!HasEnhancedKeyUsage(certificate, "1.3.6.1.5.5.7.3.2"))
                {
                    errors.Add($"{SectionName}:ClientCertificatePath must be valid for TLS client authentication.");
                }

                var now = DateTimeOffset.UtcNow;
                if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
                {
                    errors.Add($"{SectionName}:ClientCertificatePath is not currently valid.");
                }

                using var root = LoadCertificate(TrustedServerCertificatePath);
                var basicConstraints = root.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
                if (basicConstraints?.CertificateAuthority != true)
                {
                    errors.Add($"{SectionName}:TrustedServerCertificatePath must contain a CA certificate.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
                errors.Add($"{SectionName} certificate material could not be loaded: {exception.Message}");
            }
        }

        return errors;
    }

    private static void ValidateFile(string path, string propertyName, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            errors.Add($"{SectionName}:{propertyName} must reference an existing private runtime file.");
        }
    }

    internal static X509Certificate2 LoadClientCertificate(CentralPmsStatutoryServicePrincipalOptions options)
    {
        var password = File.ReadAllText(options.ClientCertificatePasswordFile).TrimEnd('\r', '\n');
        return new X509Certificate2(
            options.ClientCertificatePath,
            password,
            X509KeyStorageFlags.EphemeralKeySet);
    }

    internal static X509Certificate2 LoadCertificate(string path) =>
        new(path);

    private static bool HasEnhancedKeyUsage(X509Certificate2 certificate, string requiredOid) =>
        certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(static extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(oid => string.Equals(oid.Value, requiredOid, StringComparison.Ordinal));
}

/// <summary>
/// Returns specific startup failures for incomplete statutory mTLS deployment configuration.
/// </summary>
public sealed class CentralPmsStatutoryServicePrincipalOptionsValidator
    : IValidateOptions<CentralPmsStatutoryServicePrincipalOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(
        string? name,
        CentralPmsStatutoryServicePrincipalOptions options)
    {
        _ = name;
        ArgumentNullException.ThrowIfNull(options);

        var errors = options.Validate();
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// Presents the configured client credential and trusts only the configured local Central PMS CA.
/// </summary>
public sealed class CentralPmsStatutoryServicePrincipalHttpMessageHandler : HttpClientHandler
{
    private readonly X509Certificate2? _clientCertificate;
    private readonly X509Certificate2? _trustedServerRoot;

    /// <summary>
    /// Initializes the handler from deployment-owned statutory transport configuration.
    /// </summary>
    public CentralPmsStatutoryServicePrincipalHttpMessageHandler(
        CentralPmsStatutoryServicePrincipalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.UseServerDerivedServicePrincipal)
        {
            return;
        }

        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", validationErrors));
        }

        _clientCertificate = CentralPmsStatutoryServicePrincipalOptions.LoadClientCertificate(options);
        _trustedServerRoot = CentralPmsStatutoryServicePrincipalOptions.LoadCertificate(options.TrustedServerCertificatePath);
        ClientCertificateOptions = ClientCertificateOption.Manual;
        ClientCertificates.Add(_clientCertificate);
        ServerCertificateCustomValidationCallback = ValidateCentralPmsServerCertificate;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _clientCertificate?.Dispose();
            _trustedServerRoot?.Dispose();
        }
    }

    private bool ValidateCentralPmsServerCertificate(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        _ = request;
        _ = chain;

        if (certificate is null || _trustedServerRoot is null ||
            (sslPolicyErrors & (SslPolicyErrors.RemoteCertificateNameMismatch |
                                SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
        {
            return false;
        }

        using var customChain = new X509Chain();
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(_trustedServerRoot);
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return customChain.Build(certificate);
    }
}
