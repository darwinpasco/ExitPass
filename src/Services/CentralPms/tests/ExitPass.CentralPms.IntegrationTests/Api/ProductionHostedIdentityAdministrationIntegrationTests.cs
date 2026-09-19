using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OtpNet;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ProductionHostedIdentityAdministrationIntegrationTests
{
    private static readonly byte[] TotpProtectionKey = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("exitpass-i021-hosted-test-protection-key"));
    private static readonly byte[] TotpSecret = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("exitpass-i021-hosted-test-totp-secret"))[..20];
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public ProductionHostedIdentityAdministrationIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ProductionHost_AuthenticatesMutatesRevokesAndRejectsFixtureIdentityHeaders()
    {
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var seed = await SeedOrdinaryAdministratorAsync(password);
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, password,
                HumanSessionAudiences.ManagementPlatform, CurrentTotpCode()))
        };
        login.Headers.Add("Origin", "https://localhost");
        var loginResponse = await client.SendAsync(login);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var authenticated = await loginResponse.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        authenticated!.Authenticated.Should().BeTrue();
        authenticated.Session!.MfaRequired.Should().BeTrue();
        authenticated.Session.MfaSatisfied.Should().BeTrue();
        var csrf = loginResponse.Headers.GetValues("X-CSRF-Token").Single();

        var rolesResponse = await client.GetAsync("/v1/management-platform/identity/roles");
        rolesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await rolesResponse.Content.ReadFromJsonAsync<IdentityRoleDefinition[]>();
        roles.Should().NotBeNull().And.HaveCount(8);
        foreach (var role in roles!)
        {
            ApprovedIdentityRoleCatalog.TryGetPolicy(role.Code, out var policy).Should().BeTrue();
            role.ApplicationAccess.Should().Equal(policy!.AllowedApplicationAudiences);
            role.ScopePolicy!.AllowedScopeTypes.Should().Equal(policy.AllowedAssignmentScopes);
            role.ScopePolicy.AssignmentRequired.Should().BeTrue();
            role.ScopePolicy.DefaultScope.Should().Be(policy.DefaultAssignmentScope);
        }

        var detailResponse = await client.GetAsync($"/v1/management-platform/identity/users/{seed.UserId:D}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<IdentityUserDetail>();
        detail!.User.UserReference.Should().Be(seed.UserId);

        var updatedDisplayName = "I-021 Hosted Administrator Updated";
        using var missingCsrfRequest = new HttpRequestMessage(HttpMethod.Patch,
            $"/v1/management-platform/identity/users/{seed.UserId:D}")
        {
            Content = JsonContent.Create(new UpdateIdentityUserRequest(updatedDisplayName, null, null,
                detail.User.EffectiveFrom, null, detail.User.RowVersion, "I021_HOSTED_UPDATE"))
        };
        missingCsrfRequest.Headers.Add("Origin", "https://localhost");
        (await client.SendAsync(missingCsrfRequest)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updateResponse = await SendMutationAsync(client, HttpMethod.Patch,
            $"/v1/management-platform/identity/users/{seed.UserId:D}",
            new UpdateIdentityUserRequest(updatedDisplayName, null, null, detail.User.EffectiveFrom, null,
                detail.User.RowVersion, "I021_HOSTED_UPDATE"), csrf);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<IdentityUserSummary>();
        updated!.DisplayName.Should().Be(updatedDisplayName);
        (await ReadLatestProfileUpdateActorAsync(seed.UserId)).Should().Be(seed.UserId);

        var restoreResponse = await SendMutationAsync(client, HttpMethod.Patch,
            $"/v1/management-platform/identity/users/{seed.UserId:D}",
            new UpdateIdentityUserRequest(seed.DisplayName, null, null, updated.EffectiveFrom, null,
                updated.RowVersion, "I021_HOSTED_RESTORE"), csrf);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var revokeResponse = await SendMutationAsync(client, HttpMethod.Post,
            $"/v1/management-platform/identity/users/{seed.UserId:D}/sessions/{authenticated.Session.SessionReference:D}/revoke",
            new RevokeIdentitySessionRequest("I021_HOSTED_REVOKE"), csrf);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/v1/management-platform/identity/users")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        using var fixtureHeaderClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        fixtureHeaderClient.DefaultRequestHeaders.Add("X-ExitPass-User-Id", seed.UserId.ToString("D"));
        fixtureHeaderClient.DefaultRequestHeaders.Add("X-ExitPass-Permissions", "user.manage");
        var fixtureHeaderResponse = await fixtureHeaderClient.GetAsync("/v1/management-platform/identity/users");
        fixtureHeaderResponse.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task ProductionHost_RejectsUnsupportedUserTypeBeforePersistence()
    {
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var seed = await SeedOrdinaryAdministratorAsync(password);
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, password,
                HumanSessionAudiences.ManagementPlatform, CurrentTotpCode()))
        };
        login.Headers.Add("Origin", "https://localhost");
        var loginResponse = await client.SendAsync(login);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = loginResponse.Headers.GetValues("X-CSRF-Token").Single();
        var unsupportedUsername = $"unsupported.{Guid.NewGuid():N}";

        var response = await SendMutationAsync(client, HttpMethod.Post,
            "/v1/management-platform/identity/users",
            new CreateIdentityUserRequest(
                unsupportedUsername,
                "Unsupported User Type",
                null,
                null,
                Guid.NewGuid(),
                "SITE",
                Guid.NewGuid(),
                null,
                DateTimeOffset.UtcNow,
                null,
                "I021_USER_TYPE_VALIDATION",
                $"unsupported-{Guid.NewGuid():N}",
                "HUMAN"),
            csrf);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<IdentityAdministrationErrorResponse>();
        error!.Classification.Should().Be("IDENTITY_ADMIN_INVALID_REQUEST");
        error.Message.Should().Contain("userType");
        error.Retryable.Should().BeFalse();
        (await ReadUserCountByUsernameAsync(unsupportedUsername)).Should().Be(0);
    }

    [Fact]
    public async Task ProductionHost_AdministratorMfaEndpoints_ReturnProvisioningOnlyForSetupAndReset()
    {
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var actor = await SeedOrdinaryAdministratorAsync(password);
        var target = await SeedOrdinaryAdministratorAsync(Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(actor.Username, password,
                HumanSessionAudiences.ManagementPlatform, CurrentTotpCode()))
        };
        login.Headers.Add("Origin", "https://localhost");
        var loginResponse = await client.SendAsync(login);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = loginResponse.Headers.GetValues("X-CSRF-Token").Single();

        var statusResponse = await client.GetAsync(
            $"/v1/management-platform/identity/users/{target.UserId:D}/mfa-status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await statusResponse.Content.ReadFromJsonAsync<IdentityMfaStatus>();
        var resetResponse = await SendMutationAsync(client, HttpMethod.Post,
            $"/v1/management-platform/identity/users/{target.UserId:D}/mfa-authenticators/reset",
            new ProvisionIdentityMfaRequest(status!.RowVersion, "HOSTED_ADMIN_RESET"), csrf);

        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK, await resetResponse.Content.ReadAsStringAsync());
        var reset = await resetResponse.Content.ReadFromJsonAsync<IdentityMfaProvisioningResult>();
        reset!.MfaStatus.Status.Should().Be("ACTIVE");
        reset.Provisioning.DisplayOnce.Should().BeTrue();
        reset.Provisioning.TotpSharedSecret.Should().NotBeNullOrWhiteSpace();
        reset.Provisioning.TotpProvisioningUri.Should().Contain(reset.Provisioning.TotpSharedSecret);
        (await resetResponse.Content.ReadAsStringAsync()).Should().NotContain("protectedSecretEnvelope");

        var staleResetResponse = await SendMutationAsync(client, HttpMethod.Post,
            $"/v1/management-platform/identity/users/{target.UserId:D}/mfa-authenticators/reset",
            new ProvisionIdentityMfaRequest(status.RowVersion, "HOSTED_STALE_ADMIN_RESET"), csrf);
        staleResetResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var safeStatusBody = await client.GetStringAsync(
            $"/v1/management-platform/identity/users/{target.UserId:D}/mfa-status");
        safeStatusBody.Should().NotContain(reset.Provisioning.TotpSharedSecret)
            .And.NotContain("otpauth://")
            .And.NotContain("protectedSecretEnvelope");

        var removeResponse = await SendMutationAsync(client, HttpMethod.Post,
            $"/v1/management-platform/identity/users/{target.UserId:D}/mfa-authenticators/remove",
            new RemoveIdentityMfaRequest(reset.MfaStatus.RowVersion!.Value, "HOSTED_ADMIN_REMOVE"), csrf);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var removeBody = await removeResponse.Content.ReadAsStringAsync();
        removeBody.Should().NotContain("totpSharedSecret").And.NotContain("totpProvisioningUri");
        var removed = System.Text.Json.JsonSerializer.Deserialize<IdentityMfaStatus>(removeBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        removed!.Enrolled.Should().BeFalse();

        var setupResponse = await SendMutationAsync(client, HttpMethod.Post,
            $"/v1/management-platform/identity/users/{target.UserId:D}/mfa-authenticators/setup",
            new ProvisionIdentityMfaRequest(removed.RowVersion, "HOSTED_ADMIN_SETUP"), csrf);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setupResponse.Content.ReadAsStringAsync());
        var setup = await setupResponse.Content.ReadFromJsonAsync<IdentityMfaProvisioningResult>();
        setup!.Provisioning.TotpSharedSecret.Should().NotBe(reset.Provisioning.TotpSharedSecret);
        setup.MfaStatus.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task ProductionHost_UserTypeDoesNotDetermineRoleOrScope()
    {
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var seed = await SeedOrdinaryAdministratorAsync(password);
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, password,
                HumanSessionAudiences.ManagementPlatform, CurrentTotpCode()))
        };
        login.Headers.Add("Origin", "https://localhost");
        var loginResponse = await client.SendAsync(login);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = loginResponse.Headers.GetValues("X-CSRF-Token").Single();
        var username = $"incompatible.{Guid.NewGuid():N}";

        var response = await SendMutationAsync(client, HttpMethod.Post,
            "/v1/management-platform/identity/users",
            new CreateIdentityUserRequest(
                username, "Incompatible User Role", null, null,
                seed.DelegableRoleId, "SITE", seed.SiteId, null,
                DateTimeOffset.UtcNow, null, "I021_INCOMPATIBLE_ROLE", $"incompatible-{Guid.NewGuid():N}",
                "SUPPORT_USER"),
            csrf);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<CreateIdentityUserResult>();
        created!.User.Status.Should().Be("ACTIVE");
        created.OneTimeBootstrap.Should().NotBeNull();
        created.OneTimeBootstrap!.PasswordChangeRequired.Should().BeTrue();
        created.OneTimeBootstrap.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
        created.OneTimeBootstrap.TotpSharedSecret.Should().NotBeNullOrWhiteSpace();
        created.OneTimeBootstrap.TotpProvisioningUri.Should().Contain(created.OneTimeBootstrap.TotpSharedSecret);
        var storedExpiry = await ReadTemporaryPasswordExpiryAsync(created.User.UserReference);
        (storedExpiry - created.OneTimeBootstrap.TemporaryPasswordExpiresAt).Duration()
            .Should().BeLessThan(TimeSpan.FromMilliseconds(1));
        var userRead = await client.GetAsync($"/v1/management-platform/identity/users/{created.User.UserReference:D}");
        userRead.StatusCode.Should().Be(HttpStatusCode.OK);
        var userBody = await userRead.Content.ReadAsStringAsync();
        userBody.Should().NotContain(created.OneTimeBootstrap.TemporaryPassword);
        userBody.Should().NotContain(created.OneTimeBootstrap.TotpSharedSecret);
        (await ReadUserCountByUsernameAsync(username)).Should().Be(1);
    }

    private CustomWebApplicationFactory CreateProductionFactory() =>
        new CustomWebApplicationFactory()
            .WithEnvironment("Production")
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = _database.ConnectionString,
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost",
                ["HumanAuthentication:TotpProtectionKeyBase64"] = Convert.ToBase64String(TotpProtectionKey),
                ["HumanAuthentication:TotpProtectionKeyReference"] = "i021-hosted-test",
                ["HumanAuthentication:TotpProtectionKeyVersion"] = "1",
                ["CredentialChallengeDelivery:PublicAccountLifecycleBaseUrl"] = "https://accounts.exitpass.test",
                ["CentralPms:VendorPms:Provider"] = "SITE_ADAPTER",
                ["CentralPms:VendorPms:Environment"] = "INTEGRATION_TEST",
                ["CentralPms:VendorPms:CentralPmsServiceIdentityId"] =
                    "8063c159-dae6-57af-9f1f-e0a07d519fb2",
                ["CentralPms:VendorPms:AdapterSecretMountRoot"] = Path.GetTempPath(),
                ["CentralPms:VendorPms:AllowTaskOwnedHttp"] = "true"
            })
            .WithServiceOverrides(services => services.RemoveAll<IHostedService>());

    private static async Task<HttpResponseMessage> SendMutationAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body,
        string csrf)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-CSRF-Token", csrf);
        return await client.SendAsync(request);
    }

    private async Task<HostedAdminSeed> SeedOrdinaryAdministratorAsync(string password)
    {
        var options = Options.Create(new HumanAuthenticationOptions());
        var material = await new Argon2idHumanPasswordHasher(options).HashAsync(password, CancellationToken.None);
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var authenticatorId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var delegableAssignmentId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var username = $"i021.hosted.{Guid.NewGuid():N}";
        var displayName = "I-021 Hosted Administrator";
        var protectorOptions = Options.Create(new HumanAuthenticationOptions
        {
            TotpProtectionKeyBase64 = Convert.ToBase64String(TotpProtectionKey),
            TotpProtectionKeyReference = "i021-hosted-test",
            TotpProtectionKeyVersion = "1"
        });
        var protector = new AesGcmTotpSecretProtector(protectorOptions);
        var protectedSecret = protector.Protect(userId, authenticatorId, TotpSecret);
        const string sql = """
            INSERT INTO sites.site_groups (
                site_group_id, site_group_code, site_group_name, timezone_name, default_currency_code,
                site_group_status, effective_from)
            VALUES (@site_group_id, @site_group_code, 'I-021 Hosted Group', 'Asia/Manila', 'PHP',
                'ACTIVE', now() - interval '1 day');

            INSERT INTO sites.sites (
                site_id, site_group_id, site_code, site_name, site_type, timezone_name, country_code,
                site_status, effective_from)
            VALUES (@site_id, @site_group_id, @site_code, 'I-021 Hosted Site', 'OTHER', 'Asia/Manila', 'PH',
                'ACTIVE', now() - interval '1 day');

            INSERT INTO sites.real_carpark_catalog_site_groups (
                site_group_id, catalog_code, source_reference, source_sha256)
            VALUES (@site_group_id, 'PROFESSIONAL_PARKING_REAL_CARPARK_V1', 'hosted-integration-test', repeat('B', 64));

            INSERT INTO sites.real_carpark_catalog_sites (
                site_id, site_group_id, catalog_code, source_reference, source_sha256)
            VALUES (@site_id, @site_group_id, 'PROFESSIONAL_PARKING_REAL_CARPARK_V1', 'hosted-integration-test', repeat('B', 64));

            INSERT INTO identity.users (
                user_id, username, display_name, user_type, user_status, effective_from)
            VALUES (@user_id, @username, @display_name, 'INTERNAL_ADMIN', 'ACTIVE', now() - interval '1 minute');

            INSERT INTO identity.local_credentials (
                local_credential_id, user_id, credential_status, password_verifier, verifier_salt,
                verifier_algorithm_code, verifier_algorithm_version, verifier_work_factor,
                verifier_memory_kib, verifier_parallelism, activated_at, last_changed_at,
                created_by_user_id, updated_by_user_id)
            VALUES (@credential_id, @user_id, 'ACTIVE', @verifier, @salt, @algorithm, @algorithm_version,
                @work_factor, @memory_kib, @parallelism, now(), now(), @user_id, @user_id);

            INSERT INTO identity.user_mfa_authenticators (
                user_mfa_authenticator_id, user_id, authenticator_type, authenticator_status,
                protected_secret_envelope, protection_key_reference, protection_key_version,
                envelope_format_version, enrollment_started_at, activated_at,
                created_by_user_id, updated_by_user_id)
            VALUES (@authenticator_id, @user_id, 'TOTP', 'ACTIVE', @protected_secret,
                @key_reference, @key_version, @format_version, now(), now(), @user_id, @user_id);

            INSERT INTO identity.user_roles (
                user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT @assignment_id, @user_id, role_id, 'ACTIVE', 'I021_HOSTED', @user_id,
                   now() - interval '1 minute', @user_id, @user_id
            FROM identity.roles WHERE role_code = 'SYSTEM_ADMINISTRATOR';

            INSERT INTO identity.user_roles (
                user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT @delegable_assignment_id, @user_id, role_id, 'ACTIVE', 'I021_HOSTED', @user_id,
                   now() - interval '1 minute', @user_id, @user_id
            FROM identity.roles WHERE role_code = 'SITE_OPERATOR';

            INSERT INTO identity.user_role_scope_grants (
                user_role_scope_grant_id, user_role_id, scope_type, grant_status, grant_reason_code,
                effective_from, granted_by_user_id, created_by_user_id, updated_by_user_id)
            VALUES (gen_random_uuid(), @assignment_id, 'GLOBAL', 'ACTIVE', 'I021_HOSTED',
                now() - interval '1 minute', @user_id, @user_id, @user_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("credential_id", credentialId);
        command.Parameters.AddWithValue("authenticator_id", authenticatorId);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("delegable_assignment_id", delegableAssignmentId);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("site_group_code", $"I021-HG-{Guid.NewGuid():N}"[..32]);
        command.Parameters.AddWithValue("site_code", $"I021-HS-{Guid.NewGuid():N}"[..32]);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("protected_secret", protectedSecret);
        command.Parameters.AddWithValue("key_reference", protector.KeyReference);
        command.Parameters.AddWithValue("key_version", protector.KeyVersion);
        command.Parameters.AddWithValue("format_version", protector.EnvelopeFormatVersion);
        command.Parameters.AddWithValue("verifier", material.Verifier);
        command.Parameters.AddWithValue("salt", material.Salt);
        command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
        command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
        command.Parameters.AddWithValue("work_factor", material.Iterations);
        command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
        command.Parameters.AddWithValue("parallelism", material.Parallelism);
        await command.ExecuteNonQueryAsync();
        var delegableRoleId = (Guid)(await new NpgsqlCommand("SELECT role_id FROM identity.roles WHERE role_code = 'SITE_OPERATOR';", connection).ExecuteScalarAsync())!;
        return new(userId, username, displayName, delegableRoleId, siteId);
    }

    [Fact]
    public async Task ProductionHost_RequiresTotpForManagementAndRejectsWrongApplication()
    {
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var seed = await SeedOrdinaryAdministratorAsync(password);
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");

        var missingTotp = await client.PostAsJsonAsync("/v1/human-authentication/login",
            new HumanLoginRequest(seed.Username, password, HumanSessionAudiences.ManagementPlatform));
        missingTotp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await missingTotp.Content.ReadFromJsonAsync<HumanAuthenticationResponse>())!.ErrorCode
            .Should().Be("TOTP_REQUIRED");

        var wrongAudience = await client.PostAsJsonAsync("/v1/human-authentication/login",
            new HumanLoginRequest(seed.Username, password, HumanSessionAudiences.NativeParkingApp));
        wrongAudience.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await wrongAudience.Content.ReadFromJsonAsync<HumanAuthenticationResponse>())!.ErrorCode
            .Should().Be("APPLICATION_AUDIENCE_DENIED");
    }

    private static string CurrentTotpCode() =>
        new Totp(TotpSecret, 30, OtpHashMode.Sha1, 6).ComputeTotp(DateTime.UtcNow);

    private async Task<Guid?> ReadLatestProfileUpdateActorAsync(Guid userId)
    {
        const string sql = """
            SELECT actor_user_id
            FROM audit.audit_events
            WHERE event_type = 'USER_PROFILE_UPDATED'
              AND target_entity_id = @user_id
            ORDER BY occurred_at DESC
            LIMIT 1;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        return (Guid?)await command.ExecuteScalarAsync();
    }

    private async Task<long> ReadUserCountByUsernameAsync(string username)
    {
        const string sql = "SELECT count(*) FROM identity.users WHERE username = @username;";
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("username", username);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<DateTimeOffset> ReadTemporaryPasswordExpiryAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT temporary_password_expires_at FROM identity.local_credentials WHERE user_id=@user_id;", connection);
        command.Parameters.AddWithValue("user_id", userId);
        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Temporary-password expiry was not persisted.")
        };
    }

    private sealed record HostedAdminSeed(Guid UserId, string Username, string DisplayName, Guid DelegableRoleId, Guid SiteId);
}
