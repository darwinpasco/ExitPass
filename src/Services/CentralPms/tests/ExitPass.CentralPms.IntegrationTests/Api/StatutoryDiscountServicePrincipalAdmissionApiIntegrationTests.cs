using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.WebPay;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using ExitPass.CentralPms.Contracts.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.WebPay;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Production-path regression coverage for mTLS service-principal admission to the shared
/// statutory-decision contract. Fixture identity and permission headers are disabled throughout;
/// only the unrelated Vendor PMS dependency uses its integration-test provider.
/// </summary>
[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class StatutoryDiscountServicePrincipalAdmissionApiIntegrationTests
{
    private const string Endpoint = "/v1/statutory-discounts/decisions";
    private const string CertificateSelectorHeader = "X-Test-Service-Certificate";

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR", "statutory-discounts.decision.submit.webpay")]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal, "ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal")]
    public async Task Valid_mtls_service_principal_is_admitted_and_server_creates_canonical_intake(
        string sourceChannel,
        string owningService,
        string permission)
    {
        var scenario = await CreateScenarioAsync(sourceChannel, owningService);
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", sourceChannel, [permission]);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, sourceChannel, includeCertificate: true);

            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>();
            body!.SourceChannel.Should().Be(sourceChannel);
            body.DecisionCommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.AwaitingReview);
            body.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
            (await ReadIntakeSourceChannelAsync(body.StatutoryDiscountDecisionCommandId))
                .Should().Be(sourceChannel);
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Valid_mtls_service_principal_can_rediscover_pending_webpay_lifecycle_without_legacy_authority_headers()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(
                scenario,
                "CENTRAL_PMS",
                StatutoryDiscountSourceChannels.WebPay,
                [
                    "statutory-discounts.decision.submit.webpay",
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Permission
                ]);
            using var client = factory.CreateClient();
            using var intakeResponse = await SendAsync(
                client,
                scenario,
                StatutoryDiscountSourceChannels.WebPay,
                includeCertificate: true);
            intakeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await intakeResponse.Content.ReadAsStringAsync());

            using var response = await SendPendingLifecycleRediscoveryAsync(client, scenario);

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>();
            body!.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found);
            response.RequestMessage!.Headers.Contains(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName).Should().BeFalse();
            response.RequestMessage.Headers.Contains(CentralPmsRbacPolicyCatalog.PermissionsHeaderName).Should().BeFalse();
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Theory]
    [InlineData("/v1/webpay/statutory-discounts/evidence", "WebPayStatutoryEvidenceCapture", 5)]
    [InlineData("/v1/apt/statutory-discounts/evidence", "AptStatutoryEvidenceCapture", 6)]
    public void Statutory_evidence_channel_endpoints_accept_authenticated_service_principals_and_retain_rbac_metadata(
        string routePrefix,
        string policyName,
        int expectedEndpointCount)
    {
        using var factory = new CustomWebApplicationFactory()
            .WithConfigurationOverrides(ProductionOverrides())
            .WithEnvironment("IntegrationTest");
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(routePrefix, StringComparison.Ordinal) == true)
            .ToArray();

        endpoints.Should().HaveCount(expectedEndpointCount);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<ServicePrincipalEndpointMetadata>().Should().NotBeNull();
            var policy = endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>();
            policy.Should().NotBeNull();
            policy!.PolicyName.Should().Be(policyName);
        }
    }

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR", "statutory-discounts.decision.submit.webpay", "statutory-discounts.evidence.capture.webpay", "/v1/webpay/statutory-discounts/evidence")]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal, "ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal", "statutory-discounts.evidence.capture.assisted-payment-terminal", "/v1/apt/statutory-discounts/evidence")]
    public async Task Valid_mtls_service_principal_can_bootstrap_evidence_and_replay_without_duplicate_persistence(
        string sourceChannel,
        string owningService,
        string decisionPermission,
        string evidencePermission,
        string evidencePrefix)
    {
        var scenario = await CreateScenarioAsync(sourceChannel, owningService);
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", sourceChannel, [decisionPermission, evidencePermission]);
            using var client = factory.CreateClient();
            using var intakeResponse = await SendAsync(client, scenario, sourceChannel, includeCertificate: true);
            intakeResponse.StatusCode.Should().Be(HttpStatusCode.Created, await intakeResponse.Content.ReadAsStringAsync());
            var intake = await intakeResponse.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>();

            var operationKey = $"evidence-admission-{Guid.NewGuid():N}";
            using var firstResponse = await SendEvidenceBootstrapAsync(
                client, evidencePrefix, intake!.StatutoryDiscountDecisionCommandId, operationKey, includeCertificate: true);
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
            var first = await firstResponse.Content.ReadFromJsonAsync<StatutoryEvidenceChannelResponseDto>();
            first!.SourceChannel.Should().Be(sourceChannel);
            first.EvidenceRequired.Should().BeTrue();
            first.EvidenceSetReference.Should().NotBeNull();
            first.EvidenceItemReference.Should().NotBeNull();

            using var replayResponse = await SendEvidenceBootstrapAsync(
                client, evidencePrefix, intake.StatutoryDiscountDecisionCommandId, operationKey, includeCertificate: true);
            replayResponse.StatusCode.Should().Be(HttpStatusCode.OK, await replayResponse.Content.ReadAsStringAsync());
            var replay = await replayResponse.Content.ReadFromJsonAsync<StatutoryEvidenceChannelResponseDto>();
            replay!.EvidenceSetReference.Should().Be(first.EvidenceSetReference);
            replay.EvidenceItemReference.Should().Be(first.EvidenceItemReference);

            (await ReadEvidencePersistenceAsync(intake.StatutoryDiscountDecisionCommandId))
                .Should().Be((1L, 1L));
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Evidence_endpoint_missing_certificate_and_header_spoofing_fail_closed_without_persistence()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay", "statutory-discounts.evidence.capture.webpay"]);
            using var client = factory.CreateClient();
            using var intakeResponse = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);
            var intake = await intakeResponse.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>();

            using var missingCertificate = await SendEvidenceBootstrapAsync(
                client, "/v1/webpay/statutory-discounts/evidence", intake!.StatutoryDiscountDecisionCommandId,
                $"missing-{Guid.NewGuid():N}", includeCertificate: false);
            missingCertificate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await missingCertificate.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode
                .Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");

            using var spoof = new HttpRequestMessage(HttpMethod.Post, "/v1/webpay/statutory-discounts/evidence/bootstrap")
            {
                Content = JsonContent.Create(new StatutoryEvidenceChannelBootstrapRequest(
                    intake.StatutoryDiscountDecisionCommandId, $"spoof-{Guid.NewGuid():N}"))
            };
            spoof.Headers.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, scenario.ServiceIdentityId.ToString("D"));
            spoof.Headers.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, "statutory-discounts.evidence.capture.webpay");
            using var spoofResponse = await client.SendAsync(spoof);
            spoofResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await spoofResponse.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode
                .Should().Be("FIXTURE_IDENTITY_HEADER_PROHIBITED");

            (await ReadEvidencePersistenceAsync(intake.StatutoryDiscountDecisionCommandId))
                .Should().Be((0L, 0L));
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Theory]
    [InlineData("CENTRAL_PMS", "statutory-discounts.decision.submit.webpay", "/v1/webpay/statutory-discounts/evidence", HttpStatusCode.Forbidden, "CENTRAL_PMS_RBAC_FORBIDDEN")]
    [InlineData("MANAGEMENT_PLATFORM", "statutory-discounts.evidence.capture.webpay", "/v1/webpay/statutory-discounts/evidence", HttpStatusCode.Forbidden, "CENTRAL_PMS_SERVICE_PRINCIPAL_ADMISSION_DENIED")]
    [InlineData("CENTRAL_PMS", "statutory-discounts.evidence.capture.assisted-payment-terminal", "/v1/apt/statutory-discounts/evidence", HttpStatusCode.Forbidden, "CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH")]
    public async Task Evidence_endpoint_missing_permission_wrong_audience_or_wrong_channel_is_denied_without_persistence(
        string audience,
        string evidencePermission,
        string evidencePrefix,
        HttpStatusCode expectedStatus,
        string expectedError)
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var intakeFactory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var intakeClient = intakeFactory.CreateClient();
            using var intakeResponse = await SendAsync(intakeClient, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);
            var intake = await intakeResponse.Content.ReadFromJsonAsync<StatutoryDiscountDecisionResponse>();

            using var evidenceFactory = CreateFactory(scenario, audience, StatutoryDiscountSourceChannels.WebPay, [evidencePermission]);
            using var evidenceClient = evidenceFactory.CreateClient();
            using var response = await SendEvidenceBootstrapAsync(
                evidenceClient, evidencePrefix, intake!.StatutoryDiscountDecisionCommandId,
                $"denied-{Guid.NewGuid():N}", includeCertificate: true);

            response.StatusCode.Should().Be(expectedStatus, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be(expectedError);
            (await ReadEvidencePersistenceAsync(intake.StatutoryDiscountDecisionCommandId))
                .Should().Be((0L, 0L));
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Header_only_service_identity_is_unauthenticated()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName,
                scenario.ServiceIdentityId.ToString("D"));

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: false);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Missing_service_credential_is_unauthenticated()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: false);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_RBAC_UNAUTHENTICATED");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Header_only_permission_spoofing_is_rejected_in_production_composition()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName,
                "statutory-discounts.decision.submit.webpay");

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: false);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("FIXTURE_IDENTITY_HEADER_PROHIBITED");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Theory]
    [InlineData("MANAGEMENT_PLATFORM", "statutory-discounts.decision.submit.webpay", HttpStatusCode.Forbidden, "CENTRAL_PMS_SERVICE_PRINCIPAL_ADMISSION_DENIED")]
    [InlineData("CENTRAL_PMS", "", HttpStatusCode.Forbidden, "CENTRAL_PMS_RBAC_FORBIDDEN")]
    public async Task Wrong_audience_or_missing_permission_is_denied(
        string audience,
        string permission,
        HttpStatusCode expectedStatus,
        string expectedError)
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            var permissions = string.IsNullOrEmpty(permission) ? Array.Empty<string>() : new[] { permission };
            using var factory = CreateFactory(scenario, audience, StatutoryDiscountSourceChannels.WebPay, permissions);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);

            response.StatusCode.Should().Be(expectedStatus);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be(expectedError);
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Theory]
    [InlineData(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR", "statutory-discounts.decision.submit.webpay", StatutoryDiscountSourceChannels.AssistedPaymentTerminal)]
    [InlineData(StatutoryDiscountSourceChannels.AssistedPaymentTerminal, "ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal", StatutoryDiscountSourceChannels.WebPay)]
    public async Task Source_channel_mismatch_is_denied(
        string authenticatedSourceChannel,
        string owningService,
        string permission,
        string requestedSourceChannel)
    {
        var scenario = await CreateScenarioAsync(authenticatedSourceChannel, owningService);
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", authenticatedSourceChannel, [permission]);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, requestedSourceChannel, includeCertificate: true);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_SOURCE_CHANNEL_MISMATCH");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Service_principal_outside_requested_site_is_denied()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario with { ClaimedSiteId = Guid.NewGuid() },
                StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_SERVICE_PRINCIPAL_SCOPE_DENIED");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Service_principal_outside_requested_site_group_is_denied()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client,
                scenario with { ClaimedSiteGroupId = Guid.NewGuid() },
                StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("CENTRAL_PMS_SERVICE_PRINCIPAL_SCOPE_DENIED");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Theory]
    [InlineData("SUSPENDED", false, false, HttpStatusCode.Forbidden, "SERVICE_PRINCIPAL_DISABLED")]
    [InlineData("ACTIVE", true, false, HttpStatusCode.Unauthorized, "SERVICE_CREDENTIAL_EXPIRED_OR_REVOKED")]
    [InlineData("ACTIVE", false, true, HttpStatusCode.Unauthorized, "SERVICE_CREDENTIAL_EXPIRED_OR_REVOKED")]
    public async Task Disabled_principal_or_expired_or_revoked_credential_is_denied(
        string identityStatus,
        bool expireCredential,
        bool revokeCredential,
        HttpStatusCode expectedStatus,
        string expectedError)
    {
        var scenario = await CreateScenarioAsync(
            StatutoryDiscountSourceChannels.WebPay,
            "PAYMENT_ORCHESTRATOR",
            identityStatus,
            expireCredential,
            revokeCredential);
        try
        {
            using var factory = CreateFactory(scenario, "CENTRAL_PMS", StatutoryDiscountSourceChannels.WebPay,
                ["statutory-discounts.decision.submit.webpay"]);
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);

            response.StatusCode.Should().Be(expectedStatus);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be(expectedError);
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Trusted_but_unregistered_certificate_is_unauthenticated()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        using var unknownCertificate = CreateCertificate("unknown-service-principal");
        try
        {
            var accessor = new HeaderCertificateAccessor(new Dictionary<string, X509Certificate2>
            {
                ["presented"] = unknownCertificate
            });
            using var factory = new CustomWebApplicationFactory()
                .WithInternalMtls([unknownCertificate.Thumbprint], accessor)
                .WithConfigurationOverrides(ProductionOverrides())
                .WithEnvironment("IntegrationTest");
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("SERVICE_CREDENTIAL_UNKNOWN");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    [Fact]
    public async Task Untrusted_certificate_is_unauthenticated()
    {
        var scenario = await CreateScenarioAsync(StatutoryDiscountSourceChannels.WebPay, "PAYMENT_ORCHESTRATOR");
        using var untrustedCertificate = CreateCertificate("untrusted-service-principal");
        try
        {
            var accessor = new HeaderCertificateAccessor(new Dictionary<string, X509Certificate2>
            {
                ["presented"] = untrustedCertificate
            });
            var overrides = ProductionOverrides();
            overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:CertificateThumbprint"] = scenario.Certificate.Thumbprint;
            overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:CredentialReference"] = scenario.CredentialReference;
            overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:Audience"] = "CENTRAL_PMS";
            overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:SourceChannel"] = StatutoryDiscountSourceChannels.WebPay;
            overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:Permissions:0"] = "statutory-discounts.decision.submit.webpay";
            using var factory = new CustomWebApplicationFactory()
                .WithInternalMtls([scenario.Certificate.Thumbprint], accessor)
                .WithConfigurationOverrides(overrides)
                .WithEnvironment("IntegrationTest");
            using var client = factory.CreateClient();

            using var response = await SendAsync(client, scenario, StatutoryDiscountSourceChannels.WebPay, includeCertificate: true);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.ErrorCode.Should().Be("INTERNAL_CLIENT_CERTIFICATE_UNTRUSTED");
        }
        finally
        {
            await CleanupAsync(scenario);
        }
    }

    private static CustomWebApplicationFactory CreateFactory(
        AdmissionScenario scenario,
        string audience,
        string sourceChannel,
        IReadOnlyCollection<string> permissions)
    {
        var accessor = new HeaderCertificateAccessor(new Dictionary<string, X509Certificate2>
        {
            ["presented"] = scenario.Certificate
        });
        var overrides = ProductionOverrides();
        overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:CertificateThumbprint"] = scenario.Certificate.Thumbprint;
        overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:CredentialReference"] = scenario.CredentialReference;
        overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:Audience"] = audience;
        overrides["InternalSecurity:Mtls:ServicePrincipalCredentials:0:SourceChannel"] = sourceChannel;
        var index = 0;
        foreach (var permission in permissions)
        {
            overrides[$"InternalSecurity:Mtls:ServicePrincipalCredentials:0:Permissions:{index++}"] = permission;
        }

        return new CustomWebApplicationFactory()
            .WithInternalMtls([scenario.Certificate.Thumbprint], accessor)
            .WithConfigurationOverrides(overrides)
            .WithEnvironment("IntegrationTest");
    }

    private static Dictionary<string, string?> ProductionOverrides() => new()
    {
        ["CentralPms:Rbac:Enabled"] = "true",
        ["CentralPms:Rbac:AllowPermissionHeader"] = "false",
        ["CentralPms:Rbac:AllowFixtureIdentityHeaders"] = "false",
        ["CentralPms:VendorPms:Provider"] = "MOCK"
    };

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        AdmissionScenario scenario,
        string sourceChannel,
        bool includeCertificate)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(BuildRequest(scenario, sourceChannel))
        };
        request.Headers.Add("Idempotency-Key", $"svc-admission-{Guid.NewGuid():N}");
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("D"));
        if (includeCertificate)
        {
            request.Headers.Add(CertificateSelectorHeader, "presented");
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendEvidenceBootstrapAsync(
        HttpClient client,
        string evidencePrefix,
        Guid statutoryDiscountDecisionCommandId,
        string operationKey,
        bool includeCertificate)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{evidencePrefix}/bootstrap")
        {
            Content = JsonContent.Create(new StatutoryEvidenceChannelBootstrapRequest(
                statutoryDiscountDecisionCommandId,
                operationKey))
        };
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("D"));
        if (includeCertificate)
        {
            request.Headers.Add(CertificateSelectorHeader, "presented");
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendPendingLifecycleRediscoveryAsync(
        HttpClient client,
        AdmissionScenario scenario)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover")
        {
            Content = JsonContent.Create(new WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId,
                scenario.Context.ParkingSessionId,
                scenario.ClaimedSiteId,
                scenario.ClaimedSiteGroupId,
                TicketReference: null,
                PlateNumber: null,
                VendorSystemId: null,
                EntitlementType: "SENIOR_CITIZEN"))
        };
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("D"));
        request.Headers.Add(CertificateSelectorHeader, "presented");
        return await client.SendAsync(request);
    }

    private static StatutoryDiscountDecisionRequest BuildRequest(AdmissionScenario scenario, string sourceChannel) =>
        new(
            Guid.NewGuid(), sourceChannel, scenario.Context.ParkingSessionId,
            scenario.ClaimedSiteId, scenario.ClaimedSiteGroupId,
            $"TICKET-{scenario.Context.SiteCode}", "ABC1234", "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID", "OSCA", DateOnly.Parse("2030-01-01"), "SC-****-1234",
            EvidenceCaptureRequested: true,
            EvidenceReferences:
            [
                new StatutoryDiscountEvidenceReferenceRequest(
                    "SENIOR_CITIZEN_ID", "MANUAL_REFERENCE", null, null, null,
                    "evidence-ref-admission", "SC-****-1234", "VERIFIED")
            ],
            ActorUserId: Guid.Empty, OperatorDeviceBindingId: null, OperatorShiftId: null,
            RequesterAttestation: true, AttestationNotes: "Authenticated service admission proof.",
            ReasonCode: "CUSTOMER_REQUEST", Decision: null, DecisionReasonCode: null,
            ReviewerUserId: null, ReviewerAttestation: false, ApplyPayableBasis: false,
            OriginalTariffSnapshotId: null);

    private static async Task<AdmissionScenario> CreateScenarioAsync(
        string sourceChannel,
        string owningService,
        string identityStatus = "ACTIVE",
        bool expireCredential = false,
        bool revokeCredential = false)
    {
        var context = await StatutoryDiscountReviewIntegrationTestSupport.SeedPaymentContextAsync(
            $"service-principal-admission-{sourceChannel}-{Guid.NewGuid():N}");
        var serviceIdentityId = Guid.NewGuid();
        var credentialReference = $"secret://integration/statutory-admission/{Guid.NewGuid():N}";
        var certificate = CreateCertificate($"statutory-{sourceChannel.ToLowerInvariant()}");
        var retentionClassCode = $"IST_ADMISSION_{Guid.NewGuid():N}";
        var retentionPolicyVersion = "1.0";

        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity.service_identities (
                service_identity_id, service_identity_code, service_identity_name,
                identity_type, identity_status, owning_service_name,
                credential_reference, credential_type, credential_expires_at,
                effective_from, revoked_at, created_at, updated_at, row_version)
            VALUES (
                @service_identity_id, @service_identity_code, 'Statutory admission integration principal',
                'INTERNAL_SERVICE', @identity_status::identity.service_identity_status_enum, @owning_service,
                @credential_reference, 'MTLS_CERTIFICATE_REFERENCE',
                CASE WHEN @expire_credential THEN now() - interval '1 minute' ELSE now() + interval '1 day' END,
                now() - interval '1 hour',
                CASE WHEN @revoke_credential THEN now() - interval '1 minute' ELSE NULL END,
                now(), now(), 1);
            INSERT INTO sites.device_assignments (
                device_assignment_id, site_id, service_identity_id,
                assignment_type, assignment_status, assigned_at, created_at, updated_at, row_version)
            VALUES (gen_random_uuid(), @site_id, @service_identity_id,
                'SERVICE_PRINCIPAL', 'ACTIVE', now() - interval '1 hour', now(), now(), 1);
            INSERT INTO discounts.statutory_evidence_principal_scope_grants (
                actor_service_identity_id, source_channel, site_id, site_group_id,
                capture_allowed, view_allowed, review_lock_allowed, hold_allowed,
                deletion_request_allowed, grant_status, reason_code, effective_from,
                created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (
                @service_identity_id, @source_channel, @site_id, @site_group_id,
                true, true, false, false, false, 'ACTIVE', 'INTEGRATION_TEST', now() - interval '1 minute',
                @service_identity_id, @service_identity_id);
            INSERT INTO discounts.statutory_evidence_retention_policies (
                retention_class_code, retention_policy_version, policy_status, environment_scope,
                purpose_code, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (
                @retention_class_code, @retention_policy_version, 'APPROVED_ENABLED', 'LOCAL_TEST',
                'STATUTORY_EVIDENCE_SERVICE_ADMISSION_TEST', now() - interval '1 minute',
                @service_identity_id, @service_identity_id);
            """, connection);
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("service_identity_code", $"IST_SVC_ADMISSION_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("identity_status", identityStatus);
        command.Parameters.AddWithValue("owning_service", owningService);
        command.Parameters.AddWithValue("credential_reference", credentialReference);
        command.Parameters.AddWithValue("expire_credential", expireCredential);
        command.Parameters.AddWithValue("revoke_credential", revokeCredential);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        command.Parameters.AddWithValue("site_group_id", context.SiteGroupId);
        command.Parameters.AddWithValue("source_channel", sourceChannel);
        command.Parameters.AddWithValue("retention_class_code", retentionClassCode);
        command.Parameters.AddWithValue("retention_policy_version", retentionPolicyVersion);
        await command.ExecuteNonQueryAsync();

        return new AdmissionScenario(
            context,
            serviceIdentityId,
            credentialReference,
            certificate,
            context.SiteId,
            context.SiteGroupId,
            retentionClassCode,
            retentionPolicyVersion);
    }

    private static async Task<string> ReadIntakeSourceChannelAsync(Guid commandId)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT source_channel FROM operator_console.statutory_discount_service_channel_reviews WHERE statutory_discount_decision_command_id=@command_id;",
            connection);
        command.Parameters.AddWithValue("command_id", commandId);
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private static async Task<(long Sets, long Items)> ReadEvidencePersistenceAsync(Guid commandId)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT COUNT(*)
                 FROM discounts.statutory_evidence_sets evidence_set
                 WHERE evidence_set.statutory_discount_decision_command_id=@command_id),
                (SELECT COUNT(*)
                 FROM discounts.statutory_evidence_items evidence_item
                 JOIN discounts.statutory_evidence_sets evidence_set
                   ON evidence_set.statutory_evidence_set_id=evidence_item.statutory_evidence_set_id
                 WHERE evidence_set.statutory_discount_decision_command_id=@command_id);
            """, connection);
        command.Parameters.AddWithValue("command_id", commandId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task CleanupAsync(AdmissionScenario scenario)
    {
        await using var connection = new NpgsqlConnection(StatutoryDiscountReviewIntegrationTestSupport.ConnectionString);
        await connection.OpenAsync();
        await using (var grantCommand = new NpgsqlCommand(
                         """
                         DELETE FROM discounts.statutory_evidence_events
                         WHERE parking_session_id=@parking_session_id
                            OR actor_service_identity_id=@service_identity_id;
                         DELETE FROM discounts.statutory_evidence_operations
                         WHERE created_by_service_identity_id=@service_identity_id;
                         DELETE FROM discounts.statutory_evidence_items
                         WHERE statutory_evidence_set_id IN (
                             SELECT statutory_evidence_set_id
                             FROM discounts.statutory_evidence_sets
                             WHERE parking_session_id=@parking_session_id);
                         DELETE FROM discounts.statutory_evidence_sets
                         WHERE parking_session_id=@parking_session_id;
                         DELETE FROM discounts.statutory_evidence_principal_scope_grants
                         WHERE actor_service_identity_id=@service_identity_id;
                         """,
                         connection))
        {
            grantCommand.Parameters.AddWithValue("service_identity_id", scenario.ServiceIdentityId);
            grantCommand.Parameters.AddWithValue("parking_session_id", scenario.Context.ParkingSessionId);
            await grantCommand.ExecuteNonQueryAsync();
        }

        await StatutoryDiscountReviewIntegrationTestSupport.CleanupAsync(scenario.Context);

        await using (var identityCommand = new NpgsqlCommand(
                         """
                         DELETE FROM discounts.statutory_evidence_retention_policies
                         WHERE retention_class_code=@retention_class_code
                           AND retention_policy_version=@retention_policy_version;
                         DELETE FROM sites.device_assignments WHERE service_identity_id=@service_identity_id;
                         DELETE FROM identity.service_identities WHERE service_identity_id=@service_identity_id;
                         """, connection))
        {
            identityCommand.Parameters.AddWithValue("service_identity_id", scenario.ServiceIdentityId);
            identityCommand.Parameters.AddWithValue("retention_class_code", scenario.RetentionClassCode);
            identityCommand.Parameters.AddWithValue("retention_policy_version", scenario.RetentionPolicyVersion);
            await identityCommand.ExecuteNonQueryAsync();
        }
        scenario.Certificate.Dispose();
    }

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class HeaderCertificateAccessor(IReadOnlyDictionary<string, X509Certificate2> certificates)
        : IInternalClientCertificateAccessor
    {
        public Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context) =>
            context.Request.Headers.TryGetValue(CertificateSelectorHeader, out var selector) &&
            certificates.TryGetValue(selector.ToString(), out var certificate)
                ? Task.FromResult<X509Certificate2?>(certificate)
                : Task.FromResult<X509Certificate2?>(null);
    }

    private sealed record AdmissionScenario(
        PaymentTestContext Context,
        Guid ServiceIdentityId,
        string CredentialReference,
        X509Certificate2 Certificate,
        Guid ClaimedSiteId,
        Guid ClaimedSiteGroupId,
        string RetentionClassCode,
        string RetentionPolicyVersion);
}
