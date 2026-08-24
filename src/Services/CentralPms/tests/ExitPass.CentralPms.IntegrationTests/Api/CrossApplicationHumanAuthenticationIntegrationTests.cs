using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class CrossApplicationHumanAuthenticationIntegrationTests
{
    private const string Password = "correct horse battery staple";
    private const string CertificateHeader = "X-I022-Certificate";
    private static readonly Guid CentralPmsServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public CrossApplicationHumanAuthenticationIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database) =>
        _database = database;

    [Fact]
    public async Task Production_sessions_are_audience_isolated_and_live_role_scope_changes_converge()
    {
        var seed = await SeedScopedUserAsync(
            ["user.view", "statutory-discounts.evidence.review.view"],
            includeSiteScope: true,
            includeSiteGroupScope: true);
        await using var factory = ProductionFactory();
        using var management = WebClient(factory);
        using var review = WebClient(factory);

        var managementLogin = await LoginWebAsync(management, seed.Username, HumanSessionAudiences.ManagementPlatform);
        await EstablishOperatorDeviceAsync(review, seed);
        var reviewLogin = await LoginWebAsync(review, seed.Username, HumanSessionAudiences.OperatorConsole);

        managementLogin.Session!.Permissions.Should().Contain("user.view");
        managementLogin.Session.SiteReferences.Should().Contain(seed.SiteId);
        managementLogin.Session.SiteGroupReferences.Should().Contain(seed.SiteGroupId);
        managementLogin.Session.HasGlobalScope.Should().BeFalse();
        reviewLogin.Session!.Audience.Should().Be(HumanSessionAudiences.OperatorConsole);
        reviewLogin.Session.SessionReference.Should().NotBe(managementLogin.Session.SessionReference);
        reviewLogin.Session.OperatorDeviceBindingReference.Should().BeNull();
        reviewLogin.Session.OperatorShiftReference.Should().BeNull();
        (await ScalarAsync<int>("""
            SELECT count(*)::integer
            FROM operator_console.operator_session_contexts
            WHERE operator_user_id=@user_id AND site_id=@site_id AND site_group_id=@site_group_id
              AND context_status='ACTIVE';
            """, ("user_id", seed.UserId), ("site_id", seed.SiteId), ("site_group_id", seed.SiteGroupId)))
            .Should().Be(1);

        (await management.GetAsync($"/v1/operator-console/statutory-discounts/review-requests/{Guid.NewGuid():D}/evidence"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await review.GetAsync("/v1/management-platform/identity/users"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await ExecuteAsync("""
            UPDATE identity.role_permissions rp
            SET binding_status='REVOKED', revoked_at=now(), revocation_reason_code='I022_PROOF',
                revoked_by_service_identity_id=@service_id, updated_by_service_identity_id=@service_id,
                row_version=rp.row_version+1
            FROM identity.permissions p
            WHERE rp.role_id=@role_id AND rp.permission_id=p.permission_id
              AND p.permission_code='user.view' AND rp.binding_status='ACTIVE';
            UPDATE identity.user_role_scope_grants
            SET grant_status='REVOKED', revoked_at=now(), revocation_reason_code='I022_PROOF',
                revoked_by_service_identity_id=@service_id, updated_by_service_identity_id=@service_id,
                row_version=row_version+1
            WHERE user_role_id=@user_role_id AND scope_type='SITE' AND grant_status='ACTIVE';
            UPDATE identity.users SET authorization_epoch=authorization_epoch+1 WHERE user_id=@user_id;
            """, ("role_id", seed.RoleId), ("user_role_id", seed.UserRoleId), ("user_id", seed.UserId),
            ("service_id", CentralPmsServiceIdentityId));

        (await review.GetAsync("/v1/human-authentication/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await management.GetAsync("/v1/human-authentication/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Production_apt_session_is_device_site_and_permission_bound_without_payable_basis_conflation()
    {
        var seed = await SeedScopedUserAsync(AptHumanPermissionCatalog.OperationalPermissions, true, true);
        var device = await SeedAptDeviceAsync(seed.SiteId);
        using var certificate = CreateCertificate("i022-apt-client");
        await using var factory = ProductionFactory(certificate);
        using var apt = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        apt.DefaultRequestHeaders.Add(CertificateHeader, "trusted");
        apt.DefaultRequestHeaders.Add("X-ExitPass-Service-Identity-Id", device.ToString("D"));

        var loginResponse = await apt.PostAsJsonAsync("/v1/apt/human-sessions",
            new AptHumanSessionCreateRequest(seed.Username, Password, seed.SiteId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, await loginResponse.Content.ReadAsStringAsync());
        var login = await loginResponse.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        login!.Authenticated.Should().BeTrue();
        login.Session!.Audience.Should().Be(HumanSessionAudiences.Apt);
        login.Session.DeviceServiceIdentityReference.Should().Be(device);
        login.Session.Permissions.Should().Contain(AptHumanPermissionCatalog.OperationalPermissions);
        login.Session.Permissions.Should().NotContain(AptHumanPermissionCatalog.PayableBasisRead);
        login.Session.HasGlobalScope.Should().BeFalse();

        using var wrongDevice = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        wrongDevice.DefaultRequestHeaders.Add(CertificateHeader, "trusted");
        wrongDevice.DefaultRequestHeaders.Add("X-ExitPass-Service-Identity-Id", Guid.NewGuid().ToString("D"));
        wrongDevice.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ExitPass-HumanSession", login.AptSessionToken);
        (await wrongDevice.GetAsync($"/v1/apt/human-sessions/{login.Session.SessionReference:D}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ExecuteAsync("""
            UPDATE identity.user_role_scope_grants
            SET grant_status='REVOKED', revoked_at=now(), revocation_reason_code='I022_PROOF',
                revoked_by_service_identity_id=@service_id, updated_by_service_identity_id=@service_id,
                row_version=row_version+1
            WHERE user_role_id=@user_role_id AND grant_status='ACTIVE';
            UPDATE identity.users SET authorization_epoch=authorization_epoch+1 WHERE user_id=@user_id;
            """, ("user_role_id", seed.UserRoleId), ("user_id", seed.UserId),
            ("service_id", CentralPmsServiceIdentityId));

        apt.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ExitPass-HumanSession", login.AptSessionToken);
        var refreshed = await apt.GetAsync($"/v1/apt/human-sessions/{login.Session.SessionReference:D}");
        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "authorization-epoch changes revoke every affected H-006 audience without imposing Operator Console device/shift requirements");
    }

    [Fact]
    public async Task Production_operator_login_requiresServerIssuedDeviceCookie_and_liveRevocationBlocksQueue()
    {
        var seed = await SeedScopedUserAsync(
            [
                "statutory-discounts.review.queue.read",
                "statutory-discounts.review.detail.read",
                "statutory-discounts.decision.approve",
                "statutory-discounts.decision.reject"
            ],
            true,
            true);
        await InsertScopeAsync(seed.UserRoleId, "GLOBAL", null, null);
        var webPayReview = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            nameof(Production_operator_login_requiresServerIssuedDeviceCookie_and_liveRevocationBlocksQueue) + "WebPay",
            "WEBPAY",
            seed.SiteId,
            seed.SiteGroupId);
        await using var factory = ProductionFactory();
        using var operatorClient = WebClient(factory);
        using var managementClient = WebClient(factory);

        using (var missingProofLogin = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, Password, HumanSessionAudiences.OperatorConsole))
        })
        {
            missingProofLogin.Headers.Add("Origin", "https://localhost");
            var response = await operatorClient.SendAsync(missingProofLogin);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadFromJsonAsync<HumanAuthenticationResponse>())!.ErrorCode
                .Should().Be("OPERATOR_DEVICE_BINDING_REQUIRED");
        }

        var managementLogin = await LoginWebAsync(managementClient, seed.Username, HumanSessionAudiences.ManagementPlatform);
        managementLogin.Authenticated.Should().BeTrue("Management Platform has no Operator Console device or shift requirement");
        managementLogin.Session!.OperatorDeviceBindingReference.Should().BeNull();
        managementLogin.Session.OperatorShiftReference.Should().BeNull();
        var managementQueue = await managementClient.GetAsync("/v1/management-platform/statutory-benefit-requests?page=1&pageSize=1");
        managementQueue.StatusCode.Should().Be(HttpStatusCode.OK,
            "a GLOBAL Management Platform reviewer remains authorized without an Operator Console device or shift");

        var device = await EstablishOperatorDeviceAsync(operatorClient, seed);
        var operatorLogin = await LoginWebWithCsrfAsync(operatorClient, seed.Username, HumanSessionAudiences.OperatorConsole);
        var login = operatorLogin.Response;
        login.Session!.OperatorDeviceBindingReference.Should().BeNull("canonical storage references are not serialized to the browser");
        login.Session.OperatorShiftReference.Should().BeNull("canonical storage references are not serialized to the browser");
        (await ScalarAsync<Guid>("""
            SELECT operator_device_binding_id
            FROM operator_console.operator_session_contexts
            WHERE operator_user_id=@user_id AND context_status='ACTIVE'
            ORDER BY bound_at DESC LIMIT 1;
            """, ("user_id", seed.UserId))).Should().Be(device.DeviceId);
        (await ScalarAsync<Guid>("""
            SELECT operator_shift_id
            FROM operator_console.operator_session_contexts
            WHERE operator_user_id=@user_id AND context_status='ACTIVE'
            ORDER BY bound_at DESC LIMIT 1;
            """, ("user_id", seed.UserId))).Should().Be(device.ShiftId);

        var forgedReadiness = await operatorClient.PostAsJsonAsync(
            "/v1/ops/operator-console/access/readiness/evaluate",
            new OperatorConsoleAccessReadinessRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SESSION_LOOKUP",
                null,
                null,
                null,
                Guid.NewGuid(),
                null,
                new OperatorConsoleAccessReadinessClientContextDto("forged-browser", "forged-browser"),
                new OperatorConsoleAccessReadinessDevModeContextDto(true, "Development")));
        forgedReadiness.StatusCode.Should().Be(HttpStatusCode.OK, await forgedReadiness.Content.ReadAsStringAsync());
        var serverReadiness = await forgedReadiness.Content.ReadFromJsonAsync<OperatorConsoleAccessReadinessResponse>();
        serverReadiness!.OperatorReadiness.OperatorUserId.Should().Be(seed.UserId);
        serverReadiness.DeviceReadiness.OperatorDeviceBindingId.Should().Be(device.DeviceId);
        serverReadiness.ShiftReadiness.OperatorShiftId.Should().Be(device.ShiftId);
        serverReadiness.SiteReadiness.SiteId.Should().Be(seed.SiteId);
        serverReadiness.SiteReadiness.SiteGroupId.Should().Be(seed.SiteGroupId);

        var queue = await operatorClient.GetAsync(
            $"/v1/ops/operator-console/statutory-discounts/reviews?parkingSessionId={webPayReview.Context.ParkingSessionId:D}&page=1&pageSize=1");
        queue.StatusCode.Should().Be(HttpStatusCode.OK, await queue.Content.ReadAsStringAsync());
        var queueBody = await queue.Content.ReadFromJsonAsync<OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse>();
        queueBody!.Items.Should().Contain(item => item.StatutoryDiscountDecisionCommandId == webPayReview.Decision.StatutoryDiscountDecisionCommandId);

        var detail = await operatorClient.GetAsync(
            $"/v1/ops/operator-console/statutory-discounts/reviews/{webPayReview.Decision.StatutoryDiscountDecisionCommandId:D}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync());
        (await detail.Content.ReadFromJsonAsync<OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse>())!
            .EvidenceReferences.Should().NotBeEmpty();

        var approved = await DecideAsync(
            operatorClient,
            webPayReview.Decision.StatutoryDiscountDecisionCommandId,
            "APPROVE",
            "ELIGIBLE",
            "i022-h006-approve",
            operatorLogin.Csrf);
        approved.CurrentValidationStatus.Should().Be("APPROVED");
        await AssertReviewAttributionAsync(webPayReview, seed.UserId, device.DeviceId, device.ShiftId);
        await StatutoryDiscountReviewIntegrationTestSupport.RemoveReviewOnlyAsync(webPayReview.Context);

        var aptReview = await StatutoryDiscountReviewIntegrationTestSupport.SeedAwaitingReviewAsync(
            webPayReview.Context,
            "ASSISTED_PAYMENT_TERMINAL",
            "SENIOR_CITIZEN");
        var rejected = await DecideAsync(
            operatorClient,
            aptReview.Decision.StatutoryDiscountDecisionCommandId,
            "REJECT",
            "DOCUMENT_INVALID",
            "i022-h006-reject",
            operatorLogin.Csrf);
        rejected.CurrentValidationStatus.Should().Be("REJECTED");
        await AssertReviewAttributionAsync(aptReview, seed.UserId, device.DeviceId, device.ShiftId);

        using var forged = new HttpRequestMessage(HttpMethod.Get, "/v1/ops/operator-console/statutory-discounts/reviews?limit=1&offset=0");
        forged.Headers.Add("X-Operator-Device-Binding-Id", Guid.NewGuid().ToString("D"));
        var forgedResponse = await operatorClient.SendAsync(forged);
        forgedResponse.StatusCode.Should().NotBe(HttpStatusCode.OK);

        await ExecuteAsync("""
            UPDATE operator_console.operator_shifts
            SET operational_status='ENDED', active_to=now()-interval '1 second', row_version=row_version+1
            WHERE operator_shift_id=@shift_id;
            """, ("shift_id", device.ShiftId));
        var closedShift = await operatorClient.GetAsync("/v1/ops/operator-console/statutory-discounts/reviews?limit=1&offset=0");
        closedShift.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await closedShift.Content.ReadAsStringAsync()).Should().Contain("OPERATOR_SHIFT_CLOSED_OR_EXPIRED");

        await ExecuteAsync("""
            UPDATE operator_console.operator_shifts
            SET operational_status='ACTIVE', active_to=now()+interval '8 hours', row_version=row_version+1
            WHERE operator_shift_id=@shift_id;
            """, ("shift_id", device.ShiftId));
        (await LoginWebAsync(operatorClient, seed.Username, HumanSessionAudiences.OperatorConsole)).Authenticated.Should().BeTrue();

        await ExecuteAsync("""
            UPDATE operator_console.operator_device_bindings
            SET device_status='REVOKED', revoked_at=now(), revocation_reason_code='I022_PROOF', row_version=row_version+1
            WHERE operator_device_binding_id=@device_id;
            """, ("device_id", device.DeviceId));
        var revoked = await operatorClient.GetAsync("/v1/ops/operator-console/statutory-discounts/reviews?limit=1&offset=0");
        revoked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await revoked.Content.ReadAsStringAsync()).Should().Contain("OPERATOR_DEVICE_BINDING_REVOKED");

    }

    [Fact]
    public async Task Production_logout_all_revokes_each_audience_and_fixture_headers_cannot_restore_authority()
    {
        var seed = await SeedScopedUserAsync(["user.view", .. AptHumanPermissionCatalog.OperationalPermissions], true, false);
        var device = await SeedAptDeviceAsync(seed.SiteId);
        using var certificate = CreateCertificate("i022-apt-revocation-client");
        await using var factory = ProductionFactory(certificate);
        using var management = WebClient(factory);
        using var review = WebClient(factory);
        var managementLogin = await LoginWebWithCsrfAsync(management, seed.Username, HumanSessionAudiences.ManagementPlatform);
        await EstablishOperatorDeviceAsync(review, seed);
        await LoginWebAsync(review, seed.Username, HumanSessionAudiences.OperatorConsole);
        using var apt = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        apt.DefaultRequestHeaders.Add(CertificateHeader, "trusted");
        apt.DefaultRequestHeaders.Add("X-ExitPass-Service-Identity-Id", device.ToString("D"));
        var aptLoginResponse = await apt.PostAsJsonAsync("/v1/apt/human-sessions",
            new AptHumanSessionCreateRequest(seed.Username, Password, seed.SiteId));
        aptLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var aptLogin = await aptLoginResponse.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        aptLogin!.Authenticated.Should().BeTrue();

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/logout-all")
        {
            Content = JsonContent.Create(new { })
        };
        logout.Headers.Add("Origin", "https://localhost");
        logout.Headers.Add("X-CSRF-Token", managementLogin.Csrf);
        (await management.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await management.GetAsync("/v1/human-authentication/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await review.GetAsync("/v1/human-authentication/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        apt.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ExitPass-HumanSession", aptLogin.AptSessionToken);
        (await apt.GetAsync($"/v1/apt/human-sessions/{aptLogin.Session!.SessionReference:D}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var fixture = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        fixture.DefaultRequestHeaders.Add("X-ExitPass-User-Id", seed.UserId.ToString("D"));
        fixture.DefaultRequestHeaders.Add("X-ExitPass-Permissions", "user.view");
        var rejected = await fixture.GetAsync("/v1/management-platform/identity/users");
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("FIXTURE_IDENTITY_HEADER_PROHIBITED");

        var activeSessions = await ScalarAsync<int>(
            "SELECT count(*)::integer FROM identity.human_sessions WHERE user_id=@user_id AND session_status='ACTIVE';",
            ("user_id", seed.UserId));
        activeSessions.Should().Be(0);
    }

    private CustomWebApplicationFactory ProductionFactory(X509Certificate2? certificate = null)
    {
        var factory = new CustomWebApplicationFactory()
            .WithEnvironment("Production")
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["CentralPms:Rbac:Enabled"] = "true",
                ["CentralPms:Rbac:AllowPermissionHeader"] = "false",
                ["CentralPms:Rbac:AllowFixtureIdentityHeaders"] = "false",
                ["HumanAuthentication:Argon2Iterations"] = "1",
                ["HumanAuthentication:Argon2MemoryKiB"] = "19456",
                ["HumanAuthentication:Argon2Parallelism"] = "1",
                ["HumanAuthentication:TotpProtectionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["HumanAuthentication:TotpProtectionKeyReference"] = "i022-proof-key",
                ["HumanAuthentication:TotpProtectionKeyVersion"] = "1",
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost",
                ["CentralPms:VendorPms:Provider"] = "SITE_ADAPTER",
                ["CentralPms:VendorPms:Environment"] = "INTEGRATION_TEST",
                ["CentralPms:VendorPms:CentralPmsServiceIdentityId"] = CentralPmsServiceIdentityId.ToString("D"),
                ["CentralPms:VendorPms:AdapterSecretMountRoot"] = Path.GetTempPath(),
                ["CentralPms:VendorPms:AllowTaskOwnedHttp"] = "true"
            })
            .WithServiceOverrides(services => services.RemoveAll<IHostedService>());
        return certificate is null
            ? factory
            : factory.WithInternalMtls([certificate.Thumbprint], new HeaderCertificateAccessor(certificate));
    }

    private static HttpClient WebClient(CustomWebApplicationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<HumanAuthenticationResponse> LoginWebAsync(HttpClient client, string username, string audience) =>
        (await LoginWebWithCsrfAsync(client, username, audience)).Response;

    private static async Task<(HumanAuthenticationResponse Response, string Csrf)> LoginWebWithCsrfAsync(
        HttpClient client,
        string username,
        string audience)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(username, Password, audience))
        };
        request.Headers.Add("Origin", "https://localhost");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        body!.Authenticated.Should().BeTrue();
        return (body, response.Headers.GetValues("X-CSRF-Token").Single());
    }

    private static async Task<HumanAuthenticationResponse> ReadCurrentSessionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/v1/human-authentication/session");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HumanAuthenticationResponse>())!;
    }

    private static async Task<OperatorConsoleStatutoryDiscountDecisionResponse> DecideAsync(
        HttpClient client,
        Guid decisionCommandId,
        string decision,
        string reasonCode,
        string idempotencyKey,
        string csrf)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/ops/operator-console/statutory-discounts/reviews/{decisionCommandId:D}/decision")
        {
            Content = JsonContent.Create(new OperatorConsoleCanonicalStatutoryReviewDecisionRequest(
                decision,
                reasonCode,
                ReviewerAttestation: true,
                idempotencyKey))
        };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-CSRF-Token", csrf);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OperatorConsoleStatutoryDiscountDecisionResponse>())!;
    }

    private async Task AssertReviewAttributionAsync(
        SeededServiceChannelReview review,
        Guid reviewerUserId,
        Guid deviceBindingId,
        Guid shiftId)
    {
        (await ScalarAsync<Guid>("""
            SELECT reviewer_user_id
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id=@decision_id;
            """, ("decision_id", review.Decision.StatutoryDiscountDecisionCommandId))).Should().Be(reviewerUserId);
        (await ScalarAsync<Guid>("""
            SELECT reviewer_operator_device_binding_id
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id=@decision_id;
            """, ("decision_id", review.Decision.StatutoryDiscountDecisionCommandId))).Should().Be(deviceBindingId);
        (await ScalarAsync<Guid>("""
            SELECT reviewer_operator_shift_id
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id=@decision_id;
            """, ("decision_id", review.Decision.StatutoryDiscountDecisionCommandId))).Should().Be(shiftId);
        (await ScalarAsync<int>("""
            SELECT count(*)::integer
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id=@decision_id AND reviewed_at IS NOT NULL;
            """, ("decision_id", review.Decision.StatutoryDiscountDecisionCommandId))).Should().Be(1);
    }

    private async Task<Seed> SeedScopedUserAsync(
        IReadOnlyCollection<string> permissions,
        bool includeSiteScope,
        bool includeSiteGroupScope)
    {
        var hasher = new Argon2idHumanPasswordHasher(Options.Create(new HumanAuthenticationOptions
        {
            Argon2Iterations = 1,
            Argon2MemoryKiB = 19456,
            Argon2Parallelism = 1,
            PasswordMinimumLength = 15
        }));
        var material = await hasher.HashAsync(Password, CancellationToken.None);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var userRoleId = Guid.NewGuid();
        var siteId = await ScalarAsync<Guid>(
            "SELECT site_id FROM sites.sites WHERE site_status='ACTIVE' ORDER BY site_code LIMIT 1;");
        var siteGroupId = await ScalarAsync<Guid>(
            "SELECT site_group_id FROM sites.site_groups WHERE site_group_status='ACTIVE' ORDER BY site_group_code LIMIT 1;");
        var username = $"i022.{Guid.NewGuid():N}"[..24];

        const string sql = """
            INSERT INTO identity.users (user_id,username,display_name,user_type,user_status,effective_from,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_id,@username,'I-022 integration user','SITE_OPERATOR','ACTIVE',now()-interval '1 minute',@service_id,@service_id);
            INSERT INTO identity.local_credentials (local_credential_id,user_id,credential_status,password_verifier,
                verifier_salt,verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
                verifier_memory_kib,verifier_parallelism,activated_at,last_changed_at,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@user_id,'ACTIVE',@verifier,@salt,@algorithm,@algorithm_version,@work_factor,
                @memory_kib,@parallelism,now(),now(),@service_id,@service_id);
            INSERT INTO identity.roles (role_id,role_code,role_name,role_type,role_status,is_privileged,
                requires_elevated_approval,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@role_id,@role_code,'I-022 integration role','OTHER','ACTIVE',false,false,
                now()-interval '1 minute',@service_id,@service_id);
            INSERT INTO identity.user_roles (user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_by_service_identity_id,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_role_id,@user_id,@role_id,'ACTIVE','I022_PROOF',@service_id,
                now()-interval '1 minute',@service_id,@service_id);
            INSERT INTO identity.permissions (permission_id,permission_code,permission_name,permission_description,
                permission_domain,permission_action,permission_status,is_sensitive,requires_audit,
                created_by_service_identity_id,updated_by_service_identity_id)
            SELECT gen_random_uuid(),code,code,'I-022 disposable canonical permission binding proof.',
                'OPERATOR_CONSOLE','AUTHORIZE','ACTIVE',true,true,@service_id,@service_id
            FROM unnest(@permissions::varchar[]) AS code
            ON CONFLICT (permission_code) DO NOTHING;
            INSERT INTO identity.role_permissions (role_permission_id,role_id,permission_id,binding_status,
                binding_reason_code,assigned_by_service_identity_id,effective_from,
                created_by_service_identity_id,updated_by_service_identity_id)
            SELECT gen_random_uuid(),@role_id,p.permission_id,'ACTIVE','I022_PROOF',@service_id,
                now()-interval '1 minute',@service_id,@service_id
            FROM identity.permissions p WHERE p.permission_code=ANY(@permissions);
            """;
        await ExecuteAsync(sql,
            ("user_id", userId), ("username", username), ("service_id", CentralPmsServiceIdentityId),
            ("verifier", material.Verifier), ("salt", material.Salt), ("algorithm", material.AlgorithmCode),
            ("algorithm_version", material.AlgorithmVersion), ("work_factor", material.Iterations),
            ("memory_kib", material.MemoryKiB), ("parallelism", material.Parallelism),
            ("role_id", roleId), ("role_code", $"I022_{roleId:N}"[..32]),
            ("user_role_id", userRoleId), ("permissions", permissions.ToArray()));

        if (includeSiteScope)
        {
            await InsertScopeAsync(userRoleId, "SITE", siteId, null);
        }
        if (includeSiteGroupScope)
        {
            await InsertScopeAsync(userRoleId, "SITE_GROUP", null, siteGroupId);
        }
        return new Seed(userId, username, roleId, userRoleId, siteId, siteGroupId);
    }

    private Task InsertScopeAsync(Guid userRoleId, string scopeType, Guid? siteId, Guid? siteGroupId) =>
        ExecuteAsync("""
            INSERT INTO identity.user_role_scope_grants (user_role_scope_grant_id,user_role_id,scope_type,
                site_id,site_group_id,grant_status,grant_reason_code,effective_from,
                granted_by_service_identity_id,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@user_role_id,@scope_type::identity.authorization_scope_type_enum,
                @site_id,@site_group_id,'ACTIVE','I022_PROOF',now()-interval '1 minute',@service_id,@service_id,@service_id);
            """, ("user_role_id", userRoleId), ("scope_type", scopeType),
            ("site_id", (object?)siteId ?? DBNull.Value), ("site_group_id", (object?)siteGroupId ?? DBNull.Value),
            ("service_id", CentralPmsServiceIdentityId));

    private async Task<Guid> SeedAptDeviceAsync(Guid siteId)
    {
        var deviceId = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT INTO identity.service_identities (service_identity_id,service_identity_code,service_identity_name,
                identity_type,identity_status,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@device_id,@code,@code,'DEVICE','ACTIVE',now()-interval '1 minute',@service_id,@service_id);
            INSERT INTO sites.device_assignments (device_assignment_id,site_id,service_identity_id,assignment_type,
                assignment_status,assignment_reason_code,assigned_by_service_identity_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@site_id,@device_id,'PAYMENT_DEVICE','ACTIVE','I022_PROOF',
                @service_id,@service_id,@service_id);
            """, ("device_id", deviceId), ("code", $"I022_APT_{deviceId:N}"[..32]),
            ("service_id", CentralPmsServiceIdentityId), ("site_id", siteId));
        return deviceId;
    }

    private async Task<OperatorDeviceSeed> EstablishOperatorDeviceAsync(HttpClient client, Seed seed)
    {
        var deviceId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        var proof = $"i022-provisioning-proof-{Guid.NewGuid():N}";
        var proofHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
        await ExecuteAsync("""
            INSERT INTO operator_console.hr_identity_mappings (
                hr_identity_mapping_id,user_id,hr_provider_code,external_person_id_hash,mapping_status,
                effective_from,correlation_id,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@mapping_id,@user_id,'I022',@person_hash,'ACTIVE',now()-interval '1 minute',gen_random_uuid(),@service_id,@service_id);
            INSERT INTO operator_console.operator_device_bindings (
                operator_device_binding_id,device_binding_code,device_name,site_group_id,site_id,
                browser_key_thumbprint,device_status,trust_level,binding_source,correlation_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@device_id,@device_code,'I-022 browser',@site_group_id,@site_id,@proof_hash,
                'ACTIVE','BROWSER_KEY_ONLY','I022_PROOF',gen_random_uuid(),@service_id,@service_id);
            INSERT INTO operator_console.operator_device_assignment_history (
                operator_device_assignment_history_id,operator_device_binding_id,site_group_id,site_id,
                assignment_status_code,assignment_source_code,assigned_at,effective_from,correlation_id,
                assigned_by_service_identity_id,created_by_service_identity_id)
            VALUES (gen_random_uuid(),@device_id,@site_group_id,@site_id,'ACTIVE','I022_PROOF',now(),
                now()-interval '1 minute',gen_random_uuid(),@service_id,@service_id);
            INSERT INTO operator_console.operator_shifts (
                operator_shift_id,hr_provider_code,external_shift_id_hash,hr_identity_mapping_id,operator_user_id,
                site_group_id,site_id,scheduled_start_at,scheduled_end_at,source_imported_at,import_status_code,
                source_system_code,operational_status,active_from,active_to,correlation_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@shift_id,'I022',@shift_hash,@mapping_id,@user_id,@site_group_id,@site_id,
                now()-interval '1 hour',now()+interval '8 hours',now(),'IMPORTED','I022','ACTIVE',
                now()-interval '1 hour',now()+interval '8 hours',gen_random_uuid(),@service_id,@service_id);
            """,
            ("mapping_id", mappingId), ("user_id", seed.UserId),
            ("person_hash", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()),
            ("device_id", deviceId), ("device_code", $"I022_OC_{deviceId:N}"[..32]),
            ("site_group_id", seed.SiteGroupId), ("site_id", seed.SiteId), ("proof_hash", proofHash),
            ("shift_id", shiftId), ("shift_hash", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()),
            ("service_id", CentralPmsServiceIdentityId));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operator-console/device-binding/establish")
        {
            Content = JsonContent.Create(new { proof })
        };
        request.Headers.Add("Origin", "https://localhost");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant().Should()
            .Contain("httponly").And.Contain("secure").And.Contain("samesite=strict");
        return new OperatorDeviceSeed(deviceId, shiftId);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class HeaderCertificateAccessor(X509Certificate2 certificate) : IInternalClientCertificateAccessor
    {
        public Task<X509Certificate2?> GetClientCertificateAsync(HttpContext context) =>
            Task.FromResult(context.Request.Headers[CertificateHeader] == "trusted" ? certificate : null as X509Certificate2);
    }

    private sealed record Seed(Guid UserId, string Username, Guid RoleId, Guid UserRoleId, Guid SiteId, Guid SiteGroupId);
    private sealed record OperatorDeviceSeed(Guid DeviceId, Guid ShiftId);
}
