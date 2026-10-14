using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
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
    [Theory]
    [InlineData("PaymentOrchestrator")]
    [InlineData("PAYMENT_ORCHESTRATOR")]
    public async Task Verified_webpay_owner_builds_principal_only_from_binding_and_canonical_record(
        string owningServiceName)
    {
        using var certificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                serviceIdentityId, "INTERNAL_SERVICE", "ACTIVE", owningServiceName,
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
    public async Task Payment_orchestrator_owner_is_denied_for_assisted_payment_terminal_source()
    {
        using var certificate = CreateCertificate();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                Guid.NewGuid(), "INTERNAL_SERVICE", "ACTIVE", "PaymentOrchestrator",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [], []));
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithServiceMetadata();

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "webpay", "ASSISTED_PAYMENT_TERMINAL", [])),
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

    [Fact]
    public async Task Matching_apt_service_and_human_identities_are_preserved_as_independent_principals()
    {
        using var certificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                serviceIdentityId, "INTERNAL_SERVICE", "ACTIVE", "ASSISTED_PAYMENT_TERMINAL",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [siteId], [siteGroupId]));
        var nextCalled = false;
        var middleware = new InternalMtlsMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = ContextWithServiceMetadata();
        context.User = AptHumanPrincipal(userId, serviceIdentityId, siteId, siteGroupId);

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "apt", "ASSISTED_PAYMENT_TERMINAL",
                ["statutory-discounts.decision.submit.assisted-payment-terminal"])),
            new FixedCertificateAccessor(certificate),
            repository);

        nextCalled.Should().BeTrue();
        context.User.Identities.Select(identity => identity.AuthenticationType).Should().Equal(
            "InternalMtlsServicePrincipal",
            HumanSessionAuthenticationHandler.SchemeName);
        context.User.Identities.Single(identity => identity.AuthenticationType == "InternalMtlsServicePrincipal")
            .FindFirst("service_identity_id")!.Value.Should().Be(serviceIdentityId.ToString("D"));
        context.User.Identities.Single(identity => identity.AuthenticationType == HumanSessionAuthenticationHandler.SchemeName)
            .FindFirst("user_id")!.Value.Should().Be(userId.ToString("D"));
    }

    [Fact]
    public async Task Apt_human_bound_to_another_device_is_denied_before_endpoint_execution()
    {
        using var certificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                serviceIdentityId, "INTERNAL_SERVICE", "ACTIVE", "ASSISTED_PAYMENT_TERMINAL",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [], []));
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithServiceMetadata();
        context.User = AptHumanPrincipal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "apt", "ASSISTED_PAYMENT_TERMINAL",
                ["statutory-discounts.decision.submit.assisted-payment-terminal"])),
            new FixedCertificateAccessor(certificate),
            repository);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync())
            .Should().Contain("APT_HUMAN_DEVICE_BINDING_MISMATCH");
    }

    [Fact]
    public async Task Apt_service_rejects_human_session_for_another_audience()
    {
        using var certificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                serviceIdentityId, "INTERNAL_SERVICE", "ACTIVE", "ASSISTED_PAYMENT_TERMINAL",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [], []));
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithServiceMetadata();
        context.User = AptHumanPrincipal(
            Guid.NewGuid(), serviceIdentityId, Guid.NewGuid(), Guid.NewGuid(), audience: "OPERATOR_CONSOLE");

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "apt", "ASSISTED_PAYMENT_TERMINAL",
                ["statutory-discounts.decision.submit.assisted-payment-terminal"])),
            new FixedCertificateAccessor(certificate),
            repository);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync())
            .Should().Contain("APT_HUMAN_DEVICE_BINDING_MISMATCH");
    }

    [Fact]
    public async Task Webpay_service_principal_rejects_human_cashier_identity()
    {
        using var certificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServicePrincipalAuthenticationAsync("secret://webpay", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsServicePrincipalAuthenticationRecord(
                serviceIdentityId, "INTERNAL_SERVICE", "ACTIVE", "PaymentOrchestrator",
                "MTLS_CERTIFICATE_REFERENCE", true, true, [], []));
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithServiceMetadata();
        context.User = AptHumanPrincipal(Guid.NewGuid(), serviceIdentityId, Guid.NewGuid(), Guid.NewGuid());

        await middleware.InvokeAsync(
            context,
            Options.Create(OptionsFor(certificate, "webpay", "WEBPAY",
                ["statutory-discounts.decision.submit.webpay"])),
            new FixedCertificateAccessor(certificate),
            repository);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync())
            .Should().Contain("WEBPAY_HUMAN_SESSION_PROHIBITED");
    }

    [Fact]
    public async Task Governed_apt_certificate_builds_device_principal_from_current_canonical_version_and_scope()
    {
        using var certificate = CreateCertificate();
        var fingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        var serviceIdentityId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetGovernedDeviceAuthenticationAsync(
                $"device-mtls-sha256:{fingerprint}", Arg.Any<CancellationToken>())
            .Returns(new CentralPmsGovernedDeviceAuthenticationRecord(
                serviceIdentityId, deviceId, "ASSISTED_PAYMENT_TERMINAL", 3,
                "ASSISTED_PAYMENT_TERMINAL", [siteId], [siteGroupId]));
        var nextCalled = false;
        var middleware = new InternalMtlsMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = ContextWithGovernedAptMetadata();

        await middleware.InvokeAsync(context,
            Options.Create(new InternalMtlsOptions { Enabled = true, RequireClientCertificate = true }),
            new FixedCertificateAccessor(certificate), repository);

        nextCalled.Should().BeTrue();
        context.User.Identity!.AuthenticationType.Should().Be("GovernedDeviceMtls");
        context.User.FindFirst("service_identity_id")!.Value.Should().Be(serviceIdentityId.ToString("D"));
        context.User.FindFirst("device_id")!.Value.Should().Be(deviceId.ToString("D"));
        context.User.FindFirst("device_type")!.Value.Should().Be("ASSISTED_PAYMENT_TERMINAL");
        context.User.FindFirst("device_credential_version")!.Value.Should().Be("3");
        context.User.FindFirst("site_id")!.Value.Should().Be(siteId.ToString("D"));
        context.User.FindFirst("site_group_id")!.Value.Should().Be(siteGroupId.ToString("D"));
    }

    [Fact]
    public async Task Rotated_governed_apt_cannot_fall_back_to_legacy_static_certificate_trust()
    {
        using var staleCertificate = CreateCertificate();
        var serviceIdentityId = Guid.NewGuid();
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetGovernedDeviceAuthenticationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CentralPmsGovernedDeviceAuthenticationRecord?)null);
        repository.RequiresGovernedDeviceCertificateAsync(serviceIdentityId, Arg.Any<CancellationToken>())
            .Returns(true);
        var options = new InternalMtlsOptions { Enabled = true, RequireClientCertificate = true };
        options.TrustedClientThumbprints.Add(staleCertificate.Thumbprint);
        var middleware = new InternalMtlsMiddleware(_ => throw new InvalidOperationException("next must not run"));
        var context = ContextWithGovernedAptMetadata();
        context.Request.Headers[CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName] = serviceIdentityId.ToString("D");

        await middleware.InvokeAsync(context, Options.Create(options),
            new FixedCertificateAccessor(staleCertificate), repository);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (context.User.Identity?.IsAuthenticated ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task Governed_apt_endpoint_preserves_existing_non_mtls_test_profile()
    {
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        var nextCalled = false;
        var middleware = new InternalMtlsMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(ContextWithGovernedAptMetadata(),
            Options.Create(new InternalMtlsOptions { Enabled = false }),
            new FixedCertificateAccessor(null), repository);

        nextCalled.Should().BeTrue();
        await repository.DidNotReceiveWithAnyArgs()
            .GetGovernedDeviceAuthenticationAsync(default!, default);
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

    private static DefaultHttpContext ContextWithGovernedAptMetadata()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new InternalServiceEndpointMetadata(), new GovernedAptDeviceEndpointMetadata()),
            "governed apt endpoint"));
        return context;
    }

    private static ClaimsPrincipal AptHumanPrincipal(
        Guid userId,
        Guid deviceServiceIdentityId,
        Guid siteId,
        Guid siteGroupId,
        string audience = "APT") =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim("user_id", userId.ToString("D")),
            new Claim("exitpass_audience", audience),
            new Claim("device_service_identity_id", deviceServiceIdentityId.ToString("D")),
            new Claim("site_id", siteId.ToString("D")),
            new Claim("site_group_id", siteGroupId.ToString("D")),
            new Claim(CentralPmsRbacPolicyCatalog.PermissionClaimType, "apt.cashier.operate")
        ], HumanSessionAuthenticationHandler.SchemeName));

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
