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
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ProductionHostedIdentityAdministrationIntegrationTests
{
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
        using var factory = new CustomWebApplicationFactory()
            .WithEnvironment("Production")
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost"
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, password, HumanSessionAudiences.ManagementPlatform))
        };
        login.Headers.Add("Origin", "https://localhost");
        var loginResponse = await client.SendAsync(login);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var authenticated = await loginResponse.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        authenticated!.Authenticated.Should().BeTrue();
        authenticated.Session!.MfaRequired.Should().BeFalse();
        var csrf = loginResponse.Headers.GetValues("X-CSRF-Token").Single();

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
        using var factory = new CustomWebApplicationFactory()
            .WithEnvironment("Production")
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost"
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, password, HumanSessionAudiences.ManagementPlatform))
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
                "HUMAN",
                Guid.NewGuid(),
                "SITE",
                Guid.NewGuid(),
                null,
                DateTimeOffset.UtcNow,
                null,
                "I021_USER_TYPE_VALIDATION",
                $"unsupported-{Guid.NewGuid():N}"),
            csrf);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<IdentityAdministrationErrorResponse>();
        error!.Classification.Should().Be("IDENTITY_ADMIN_INVALID_REQUEST");
        error.Message.Should().Contain("userType");
        error.Retryable.Should().BeFalse();
        (await ReadUserCountByUsernameAsync(unsupportedUsername)).Should().Be(0);
    }

    [Fact]
    public async Task ProductionHost_RejectsIncompatibleUserTypeAndRoleBeforePersistence()
    {
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var seed = await SeedOrdinaryAdministratorAsync(password);
        using var factory = new CustomWebApplicationFactory()
            .WithEnvironment("Production")
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost"
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(seed.Username, password, HumanSessionAudiences.ManagementPlatform))
        };
        login.Headers.Add("Origin", "https://localhost");
        var loginResponse = await client.SendAsync(login);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = loginResponse.Headers.GetValues("X-CSRF-Token").Single();
        var username = $"incompatible.{Guid.NewGuid():N}";

        var response = await SendMutationAsync(client, HttpMethod.Post,
            "/v1/management-platform/identity/users",
            new CreateIdentityUserRequest(
                username, "Incompatible User Role", null, null, "SUPPORT_USER",
                seed.DelegableRoleId, "SITE", seed.SiteId, null,
                DateTimeOffset.UtcNow, null, "I021_INCOMPATIBLE_ROLE", $"incompatible-{Guid.NewGuid():N}"),
            csrf);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<IdentityAdministrationErrorResponse>();
        error!.Classification.Should().Be("USER_TYPE_ROLE_INCOMPATIBLE");
        error.Retryable.Should().BeFalse();
        (await ReadUserCountByUsernameAsync(username)).Should().Be(0);
    }

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
        var roleId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var delegableAssignmentId = Guid.NewGuid();
        var username = $"i021.hosted.{Guid.NewGuid():N}";
        var displayName = "I-021 Hosted Administrator";
        const string sql = """
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

            INSERT INTO identity.roles (
                role_id, role_code, role_name, role_type, role_status, is_privileged,
                requires_elevated_approval, effective_from)
            VALUES (@role_id, @role_code, 'I-021 Hosted Administrator', 'OTHER', 'ACTIVE', false, false, now() - interval '1 minute');

            INSERT INTO identity.user_roles (
                user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            VALUES (@assignment_id, @user_id, @role_id, 'ACTIVE', 'I021_HOSTED', @user_id,
                now() - interval '1 minute', @user_id, @user_id);

            INSERT INTO identity.role_permissions (
                role_permission_id, role_id, permission_id, binding_status, binding_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT gen_random_uuid(), @role_id, permission_id, 'ACTIVE', 'I021_HOSTED', @user_id,
                   now() - interval '1 minute', @user_id, @user_id
            FROM identity.permissions
            WHERE permission_code IN ('user.view', 'user.manage',
                'identity.role-assignment.manage', 'identity.scope-assignment.manage',
                'human-authentication.session.admin.view', 'human-authentication.session.admin.revoke');

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
        command.Parameters.AddWithValue("role_id", roleId);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("delegable_assignment_id", delegableAssignmentId);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("role_code", $"I021_HOSTED_{Guid.NewGuid():N}"[..40]);
        command.Parameters.AddWithValue("verifier", material.Verifier);
        command.Parameters.AddWithValue("salt", material.Salt);
        command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
        command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
        command.Parameters.AddWithValue("work_factor", material.Iterations);
        command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
        command.Parameters.AddWithValue("parallelism", material.Parallelism);
        await command.ExecuteNonQueryAsync();
        var delegableRoleId = (Guid)(await new NpgsqlCommand("SELECT role_id FROM identity.roles WHERE role_code = 'SITE_OPERATOR';", connection).ExecuteScalarAsync())!;
        var siteId = (Guid)(await new NpgsqlCommand("SELECT site_id FROM sites.sites WHERE site_status = 'ACTIVE' ORDER BY site_id LIMIT 1;", connection).ExecuteScalarAsync())!;
        return new(userId, username, displayName, delegableRoleId, siteId);
    }

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

    private sealed record HostedAdminSeed(Guid UserId, string Username, string DisplayName, Guid DelegableRoleId, Guid SiteId);
}
