using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExitPass.PaymentOrchestrator.Infrastructure.Integrations;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Integrations;

public sealed class CentralPmsStatutoryServicePrincipalOptionsTests
{
    [Fact]
    public void Validate_WhenModeDisabled_DoesNotRequireCertificateMaterial()
    {
        var options = new CentralPmsStatutoryServicePrincipalOptions();

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Validate_WhenModeEnabledWithoutMaterial_FailsClosed()
    {
        var options = new CentralPmsStatutoryServicePrincipalOptions
        {
            UseServerDerivedServicePrincipal = true,
            BaseUrl = "http://central-pms.internal:8080"
        };

        var errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("absolute HTTPS URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ClientCertificatePath", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ClientCertificatePasswordFile", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TrustedServerCertificatePath", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionsValidator_WhenModeEnabledWithoutMaterial_ReturnsSpecificStartupFailures()
    {
        var options = new CentralPmsStatutoryServicePrincipalOptions
        {
            UseServerDerivedServicePrincipal = true,
            BaseUrl = "http://central-pms.internal:8080"
        };

        var result = new CentralPmsStatutoryServicePrincipalOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("absolute HTTPS URL", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("ClientCertificatePath", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Handler_WithDedicatedClientCertificate_LoadsPrivateCredential()
    {
        using var material = CertificateMaterial.Create(clientAuthentication: true);
        var options = material.Options();

        Assert.Empty(options.Validate());
        using var handler = new CentralPmsStatutoryServicePrincipalHttpMessageHandler(options);

        Assert.Equal(ClientCertificateOption.Manual, handler.ClientCertificateOptions);
        Assert.Single(handler.ClientCertificates);
        Assert.True(Assert.IsType<X509Certificate2>(handler.ClientCertificates[0]).HasPrivateKey);
    }

    [Fact]
    public void Validate_WhenCertificateIsNotForClientAuthentication_FailsClosed()
    {
        using var material = CertificateMaterial.Create(clientAuthentication: false);

        var errors = material.Options().Validate();

        Assert.Contains(errors, error => error.Contains("TLS client authentication", StringComparison.Ordinal));
    }

    private sealed class CertificateMaterial : IDisposable
    {
        private readonly string _directory;

        private CertificateMaterial(string directory)
        {
            _directory = directory;
        }

        public static CertificateMaterial Create(bool clientAuthentication)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"exitpass-statutory-mtls-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var material = new CertificateMaterial(directory);
            material.WriteCertificates(clientAuthentication);
            return material;
        }

        public CentralPmsStatutoryServicePrincipalOptions Options() => new()
        {
            UseServerDerivedServicePrincipal = true,
            BaseUrl = "https://central-pms.internal:8443",
            ClientCertificatePath = Path.Combine(_directory, "client.pfx"),
            ClientCertificatePasswordFile = Path.Combine(_directory, "client-password"),
            TrustedServerCertificatePath = Path.Combine(_directory, "root.cer")
        };

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        private void WriteCertificates(bool clientAuthentication)
        {
            using var rootKey = RSA.Create(2048);
            var rootRequest = new CertificateRequest(
                "CN=ExitPass unit root",
                rootKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
            using var root = rootRequest.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));

            using var clientKey = RSA.Create(2048);
            var clientRequest = new CertificateRequest(
                "CN=ExitPass Payment Orchestrator unit",
                clientKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            clientRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            clientRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            clientRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new(clientAuthentication ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1")
                },
                true));
            using var publicClient = clientRequest.Create(
                root,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1),
                RandomNumberGenerator.GetBytes(16));
            using var client = publicClient.CopyWithPrivateKey(clientKey);

            const string password = "unit-test-password";
            File.WriteAllBytes(Path.Combine(_directory, "root.cer"), root.Export(X509ContentType.Cert));
            File.WriteAllBytes(Path.Combine(_directory, "client.pfx"), client.Export(X509ContentType.Pfx, password));
            File.WriteAllText(Path.Combine(_directory, "client-password"), password);
        }
    }
}
