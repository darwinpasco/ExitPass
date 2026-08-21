using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
public sealed class ManagementDashboardReportingHostedSessionIntegrationTests
{
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public ManagementDashboardReportingHostedSessionIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ProductionHost_UsesHumanSessionPermissionScopeAndAuditForDashboardReads()
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var seed = await SeedDashboardReaderAsync(password);
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        await LoginAsync(client, seed.Username, password);

        using var catalog = await client.GetAsync("/v1/management-platform/dashboard/catalog");
        using var overview = await client.GetAsync(
            $"/v1/management-platform/dashboard/operational-overview?scopeType=SITE&scopeReference={seed.SiteId:D}");
        using var wrongSite = await client.GetAsync(
            $"/v1/management-platform/dashboard/operational-overview?scopeType=SITE&scopeReference={seed.OtherSiteId:D}");
        using var wrongSiteGroup = await client.GetAsync(
            $"/v1/management-platform/dashboard/operational-overview?scopeType=SITE_GROUP&scopeReference={seed.SiteGroupId:D}");

        catalog.StatusCode.Should().Be(HttpStatusCode.OK);
        overview.StatusCode.Should().Be(HttpStatusCode.OK);
        wrongSite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongSiteGroup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await overview.Content.ReadFromJsonAsync<ManagementDashboardOperationalOverviewResponse>();
        body!.EffectiveScope.ScopeReference.Should().Be(seed.SiteId);
        body.Sections.Should().Contain(section => section.SectionId == "site-operational-status");
        (await CountAuditAsync(seed.UserId, "MANAGEMENT_DASHBOARD_CATALOG_READ")).Should().BeGreaterThan(0);
        (await CountAuditAsync(seed.UserId, "MANAGEMENT_DASHBOARD_OVERVIEW_READ")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ProductionHost_RejectsStaleAuthorizationEpochAndDisabledAccount()
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var staleSeed = await SeedDashboardReaderAsync(password);
        using var factory = CreateFactory();
        using var staleClient = CreateClient(factory);
        await LoginAsync(staleClient, staleSeed.Username, password);

        await ExecuteAsync(
            "UPDATE identity.users SET authorization_epoch = authorization_epoch + 1 WHERE user_id = @user_id;",
            staleSeed.UserId);
        (await staleClient.GetAsync("/v1/management-platform/dashboard/catalog")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var disabledPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var disabledSeed = await SeedDashboardReaderAsync(disabledPassword);
        using var disabledClient = CreateClient(factory);
        await LoginAsync(disabledClient, disabledSeed.Username, disabledPassword);
        await ExecuteAsync(
            "UPDATE identity.users SET user_status = 'SUSPENDED' WHERE user_id = @user_id;",
            disabledSeed.UserId);
        (await disabledClient.GetAsync("/v1/management-platform/dashboard/catalog")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private CustomWebApplicationFactory CreateFactory() =>
        new CustomWebApplicationFactory()
            .WithEnvironment("Production")
            .WithConfigurationOverrides(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = _database.ConnectionString,
                ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost",
                ["ManagementPlatform:DashboardReporting:Enabled"] = "true",
                ["ManagementPlatform:DashboardReporting:ProjectionStaleAfterMinutes"] = "15"
            });

    private static HttpClient CreateClient(CustomWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/human-authentication/login")
        {
            Content = JsonContent.Create(new HumanLoginRequest(username, password, HumanSessionAudiences.ManagementPlatform))
        };
        request.Headers.Add("Origin", "https://localhost");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        body!.Authenticated.Should().BeTrue();
        body.Session!.Permissions.Should().Contain([
            ManagementDashboardReportingValues.CatalogPermission,
            ManagementDashboardReportingValues.OverviewPermission]);
    }

    private async Task<DashboardReaderSeed> SeedDashboardReaderAsync(string password)
    {
        var material = await new Argon2idHumanPasswordHasher(Options.Create(new HumanAuthenticationOptions()))
            .HashAsync(password, CancellationToken.None);
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var otherSiteId = Guid.NewGuid();
        var username = $"dashboard.hosted.{Guid.NewGuid():N}";
        const string sql = """
            INSERT INTO sites.site_groups (
                site_group_id, site_group_code, site_group_name, timezone_name, default_currency_code,
                site_group_status, public_lookup_enabled, default_payment_enabled, effective_from)
            VALUES (@site_group_id, @site_group_code, 'Dashboard Hosted Group', 'Asia/Manila', 'PHP',
                'ACTIVE', false, false, now() - interval '1 minute');

            INSERT INTO sites.sites (
                site_id, site_group_id, site_code, site_name, site_type, timezone_name, country_code,
                site_status, public_lookup_enabled, payment_enabled, effective_from)
            VALUES
                (@site_id, @site_group_id, @site_code, 'Dashboard Hosted Site', 'OTHER', 'Asia/Manila', 'PH',
                 'ACTIVE', false, true, now() - interval '1 minute'),
                (@other_site_id, @site_group_id, @other_site_code, 'Dashboard Other Site', 'OTHER', 'Asia/Manila', 'PH',
                 'ACTIVE', false, false, now() - interval '1 minute');

            INSERT INTO identity.users (user_id, username, display_name, user_type, user_status, effective_from)
            VALUES (@user_id, @username, 'Dashboard Hosted Reader', 'INTERNAL_ADMIN', 'ACTIVE', now() - interval '1 minute');

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
            VALUES (@role_id, @role_code, 'Dashboard Hosted Reader', 'OTHER', 'ACTIVE', false, false,
                now() - interval '1 minute');

            INSERT INTO identity.user_roles (
                user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            VALUES (@assignment_id, @user_id, @role_id, 'ACTIVE', 'DASHBOARD_HOSTED', @user_id,
                now() - interval '1 minute', @user_id, @user_id);

            INSERT INTO identity.role_permissions (
                role_permission_id, role_id, permission_id, binding_status, binding_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT gen_random_uuid(), @role_id, permission_id, 'ACTIVE', 'DASHBOARD_HOSTED', @user_id,
                   now() - interval '1 minute', @user_id, @user_id
            FROM identity.permissions
            WHERE permission_code IN ('reports.view', 'dashboard.view');

            INSERT INTO identity.user_role_scope_grants (
                user_role_scope_grant_id, user_role_id, scope_type, site_id, grant_status,
                grant_reason_code, effective_from, granted_by_user_id, created_by_user_id, updated_by_user_id)
            VALUES (gen_random_uuid(), @assignment_id, 'SITE', @site_id, 'ACTIVE', 'DASHBOARD_HOSTED',
                now() - interval '1 minute', @user_id, @user_id, @user_id);
            """;

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("site_group_code", $"DASH-G-{Guid.NewGuid():N}"[..32]);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("site_code", $"DASH-S-{Guid.NewGuid():N}"[..32]);
        command.Parameters.AddWithValue("other_site_id", otherSiteId);
        command.Parameters.AddWithValue("other_site_code", $"DASH-O-{Guid.NewGuid():N}"[..32]);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("credential_id", credentialId);
        command.Parameters.AddWithValue("role_id", roleId);
        command.Parameters.AddWithValue("role_code", $"DASHBOARD_{Guid.NewGuid():N}"[..40]);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("verifier", material.Verifier);
        command.Parameters.AddWithValue("salt", material.Salt);
        command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
        command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
        command.Parameters.AddWithValue("work_factor", material.Iterations);
        command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
        command.Parameters.AddWithValue("parallelism", material.Parallelism);
        await command.ExecuteNonQueryAsync();
        return new DashboardReaderSeed(userId, username, siteGroupId, siteId, otherSiteId);
    }

    private async Task<long> CountAuditAsync(Guid actorUserId, string eventType)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_events WHERE actor_user_id = @user_id AND event_type = @event_type;",
            connection);
        command.Parameters.AddWithValue("user_id", actorUserId);
        command.Parameters.AddWithValue("event_type", eventType);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task ExecuteAsync(string sql, Guid userId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record DashboardReaderSeed(
        Guid UserId,
        string Username,
        Guid SiteGroupId,
        Guid SiteId,
        Guid OtherSiteId);
}
