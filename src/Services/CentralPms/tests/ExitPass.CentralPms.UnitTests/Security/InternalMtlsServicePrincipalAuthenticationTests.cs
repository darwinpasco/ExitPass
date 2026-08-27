using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Security;

public sealed class InternalMtlsServicePrincipalAuthenticationTests
{
    [Fact]
    public async Task Verified_certificate_builds_principal_only_from_binding_and_canonical_record()
    {
        using var certificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                serviceIdentityId, "INTERNAL_SERVICE", "ACTIVE", "PAYMENT_ORCHESTRATOR",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [siteId], [siteGroupId]));
        var nextCalled = false;
        var middleware = new InternalMtlsMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextWithServiceMetadata();
        context.Request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName] = Guid.NewGuid().ToString("D");
        context.Request.Headers[CentralPmsRbacPolicyCatalog.PermissionsHeaderName] = "forged.permission";

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "PAYMENT_ORCHESTRATOR", "WEBPAY",
                ["statutory-discounts.decision.submit.webpay"])),
            new FixedCertificateAccessor(certificate),
            repository);

        nextCalled.Should().BeTrue();
        context.User.Identity!.IsAuthenticated.Should().BeTrue();
        context.User.Identity.AuthenticationType.Should().Be("InternalMtlsServicePrincipal");
        context.User.FindFirst("service_identity_id")!.Value.Should().Be(serviceIdentityId.ToString("D"));
        context.User.FindFirst("exitpass_audience")!.Value.Should().Be("CENTRAL_PMS");
        context.User.FindFirst("source_channel")!.Value.Should().Be("WEBPAY");
        context.User.FindAll(CentralPmsRbacPolicyCatalog.PermissionClaimType).Select(claim => claim.Value)
            .Should().Equal("statutory-discounts.decision.submit.webpay");
        context.User.FindAll("site_id").Single().Value.Should().Be(siteId.ToString("D"));
        context.User.FindAll("site_group_id").Single().Value.Should().Be(siteGroupId.ToString("D"));
        context.User.Claims.Should().NotContain(claim => claim.Value == "forged.permission");
    }

    [Fact]
    public async Task Unregistered_credential_fails_closed_without_constructing_principal()
    {
        using var certificate = CreateCertificate();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithServiceMetadata();

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "missing", "WEBPAY", [])),
            new FixedCertificateAccessor(certificate),
            repository);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (context.User.Identity?.IsAuthenticated ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task Canonical_source_application_mismatch_is_denied()
    {
        using var certificate = CreateCertificate();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                Guid.NewGuid(), "INTERNAL_SERVICE", "ACTIVE", "ASSISTED_PAYMENT_TERMINAL",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [], []));
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithServiceMetadata();

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "webpay", "WEBPAY", [])),
            new FixedCertificateAccessor(certificate),
            repository);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (context.User.Identity?.IsAuthenticated ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task Shared_endpoint_without_certificate_defers_to_independent_human_authentication_path()
    {
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        var nextCalled = false;
        var middleware = new InternalMtlsMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextWithServiceMetadata();

        await middleware.InvokeAsync(
            context,
            Options.Create(new InternalMtlsOptions { Enabled = true }),
            new FixedCertificateAccessor(null),
            repository);

        nextCalled.Should().BeTrue();
        await repository.DidNotReceiveWithAnyArgs()
            .GetServicePrincipalAuthenticationAsync(default!, default);
    }

    private static DefaultHttpContext ContextWithServiceMetadata()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ServicePrincipalEndpointMetadata()),
            "shared statutory endpoint"));
        return context;
    }

    private static InternalMtlsOptions OptionsFor(
        X509Certificate2 certificate,
        string credentialReference,
        string sourceChannel,
        IReadOnlyCollection<string> permissions)
    {
        var options = new InternalMtlsOptions { Enabled = true, RequireClientCertificate = true };
        options.TrustedClientThumbprints.Add(certificate.Thumbprint);
        var binding = new InternalServicePrincipalCredentialBinding
        {
            CertificateThumbprint = certificate.Thumbprint,
            CredentialReference = credentialReference == "missing" ? "secret://missing" : "secret://webpay",
            Audience = "CENTRAL_PMS",
            SourceChannel = sourceChannel
        };
        foreach (var permission in permissions)
        {
            binding.Permissions.Add(permission);
        }
        options.ServicePrincipalCredentials.Add(binding);
        return options;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=service-principal-unit", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class FixedCertificateAccessor(X509Certificate2? certificate) : IInternalClientCertificateAccessor
    {
        public Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context) => Task.FromResult(certificate);
    }
}
