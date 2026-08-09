using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var reviewLogin = await LoginWebAsync(review, seed.Username, HumanSessionAudiences.OperatorConsole);

        managementLogin.Session!.Permissions.Should().Contain("user.view");
        managementLogin.Session.SiteReferences.Should().Contain(seed.SiteId);
        managementLogin.Session.SiteGroupReferences.Should().Contain(seed.SiteGroupId);
        managementLogin.Session.HasGlobalScope.Should().BeFalse();
        reviewLogin.Session!.Audience.Should().Be(HumanSessionAudiences.OperatorConsole);
        reviewLogin.Session.SessionReference.Should().NotBe(managementLogin.Session.SessionReference);

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

        var refreshed = await ReadCurrentSessionAsync(review);
        refreshed.Session!.Permissions.Should().NotContain("user.view");
        refreshed.Session.SiteReferences.Should().NotContain(seed.SiteId);
        refreshed.Session.SiteGroupReferences.Should().Contain(seed.SiteGroupId);
        refreshed.Session.HasGlobalScope.Should().BeFalse();
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
        var refreshed = await apt.GetFromJsonAsync<HumanAuthenticationResponse>(
            $"/v1/apt/human-sessions/{login.Session.SessionReference:D}");
        refreshed!.Authenticated.Should().BeTrue();
        refreshed.Session!.SiteReferences.Should().BeEmpty();
        refreshed.Session.SiteGroupReferences.Should().BeEmpty();
        refreshed.Session.HasGlobalScope.Should().BeFalse();
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
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost"
            });
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
}
