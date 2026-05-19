using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExitPass.CentralPms.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies opt-in mTLS enforcement for Central PMS internal service-to-service endpoints.
/// </summary>
public sealed class InternalMtlsSecurityIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CertificateSelectorHeader = "X-Test-Client-Certificate";
    private readonly CustomWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalMtlsSecurityIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">Default Central PMS API factory.</param>
    public InternalMtlsSecurityIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Verifies that the normal non-mTLS test profile allows existing internal endpoint calls.
    /// </summary>
    [Fact]
    public async Task InternalEndpoint_WhenMtlsDisabled_AllowsExistingTestFlow()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/v1/internal/payments/outcome/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that internal endpoints reject callers without a client certificate when mTLS is enabled.
    /// </summary>
    [Fact]
    public async Task InternalEndpoint_WhenMtlsEnabledAndNoCertificate_ReturnsUnauthorized()
    {
        using var trustedCertificate = CreateCertificate("trusted-central-pms-client");
        using var factory = CreateMtlsFactory(trustedCertificate, CreateCertificate("untrusted-central-pms-client"));
        using var client = factory.CreateClient();

        using var response = await SendInternalReadAsync(client, certificateName: null, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that internal endpoints allow callers with a trusted development certificate when mTLS is enabled.
    /// </summary>
    [Fact]
    public async Task InternalEndpoint_WhenMtlsEnabledAndTrustedCertificate_AllowsRequest()
    {
        using var trustedCertificate = CreateCertificate("trusted-central-pms-client");
        using var untrustedCertificate = CreateCertificate("untrusted-central-pms-client");
        using var factory = CreateMtlsFactory(trustedCertificate, untrustedCertificate);
        using var client = factory.CreateClient();

        using var response = await SendInternalReadAsync(client, "trusted", Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that internal endpoints reject callers with an untrusted certificate when mTLS is enabled.
    /// </summary>
    [Fact]
    public async Task InternalEndpoint_WhenMtlsEnabledAndUntrustedCertificate_ReturnsForbidden()
    {
        using var trustedCertificate = CreateCertificate("trusted-central-pms-client");
        using var untrustedCertificate = CreateCertificate("untrusted-central-pms-client");
        using var factory = CreateMtlsFactory(trustedCertificate, untrustedCertificate);
        using var client = factory.CreateClient();

        using var response = await SendInternalReadAsync(client, "untrusted", Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that mTLS enforcement preserves the caller correlation ID response header.
    /// </summary>
    [Fact]
    public async Task InternalEndpoint_WhenMtlsEnabled_PreservesCorrelationId()
    {
        using var trustedCertificate = CreateCertificate("trusted-central-pms-client");
        using var untrustedCertificate = CreateCertificate("untrusted-central-pms-client");
        using var factory = CreateMtlsFactory(trustedCertificate, untrustedCertificate);
        using var client = factory.CreateClient();
        var correlationId = Guid.Parse("70000000-0000-0000-0000-000000000001");

        using var response = await SendInternalReadAsync(client, "trusted", correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values.Should().Contain(correlationId.ToString());
    }

    /// <summary>
    /// Verifies that public endpoints are not accidentally locked when internal mTLS is enabled.
    /// </summary>
    [Fact]
    public async Task PublicEndpoint_WhenMtlsEnabled_DoesNotAccidentallyRequireClientCertificate()
    {
        using var trustedCertificate = CreateCertificate("trusted-central-pms-client");
        using var untrustedCertificate = CreateCertificate("untrusted-central-pms-client");
        using var factory = CreateMtlsFactory(trustedCertificate, untrustedCertificate);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static CustomWebApplicationFactory CreateMtlsFactory(
        X509Certificate2 trustedCertificate,
        X509Certificate2 untrustedCertificate)
    {
        var accessor = new HeaderBackedCertificateAccessor(new Dictionary<string, X509Certificate2>
        {
            ["trusted"] = trustedCertificate,
            ["untrusted"] = untrustedCertificate
        });

        return new CustomWebApplicationFactory()
            .WithInternalMtls(
                new[] { trustedCertificate.Thumbprint },
                accessor);
    }

    private static async Task<HttpResponseMessage> SendInternalReadAsync(
        HttpClient client,
        string? certificateName,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/internal/payments/outcome/{Guid.NewGuid()}");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        if (!string.IsNullOrWhiteSpace(certificateName))
        {
            request.Headers.Add(CertificateSelectorHeader, certificateName);
        }

        return await client.SendAsync(request);
    }

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class HeaderBackedCertificateAccessor : IInternalClientCertificateAccessor
    {
        private readonly IReadOnlyDictionary<string, X509Certificate2> _certificates;

        public HeaderBackedCertificateAccessor(IReadOnlyDictionary<string, X509Certificate2> certificates)
        {
            _certificates = certificates;
        }

        public Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue(CertificateSelectorHeader, out var certificateName) ||
                !_certificates.TryGetValue(certificateName.ToString(), out var certificate))
            {
                return Task.FromResult<X509Certificate2?>(null);
            }

            return Task.FromResult<X509Certificate2?>(certificate);
        }
    }
}
