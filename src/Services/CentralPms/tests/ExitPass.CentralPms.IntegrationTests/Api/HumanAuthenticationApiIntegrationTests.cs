using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class HumanAuthenticationApiIntegrationTests
{
    private static readonly Guid CentralPmsServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public HumanAuthenticationApiIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database) =>
        _database = database;

    [Fact]
    public async Task Web_login_uses_host_only_secure_cookie_no_store_and_server_derived_session()
    {
        var username = $"I020Api{Guid.NewGuid():N}"[..24];
        await SeedUserAsync(username, "correct horse battery staple");
        await using var factory = Factory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");

        var response = await client.PostAsJsonAsync("/v1/human-authentication/login",
            new HumanLoginRequest(username, "correct horse battery staple", "operator-console"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        var sessionCookie = cookies.Single(value => value.StartsWith("__Host-ExitPass-HumanSession=", StringComparison.Ordinal));
        sessionCookie.ToLowerInvariant().Should()
            .Contain("secure", Exactly.Once())
            .And.Contain("httponly", Exactly.Once())
            .And.Contain("samesite=strict", Exactly.Once())
            .And.NotContain("domain=");

        var body = await response.Content.ReadFromJsonAsync<HumanAuthenticationResponse>();
        body!.Authenticated.Should().BeTrue();
        body.AptSessionToken.Should().BeNull();
        body.Session!.Audience.Should().Be(HumanSessionAudiences.OperatorConsole);
        body.Session.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_continuation_requires_csrf_and_rotates_the_opaque_cookie()
    {
        var username = $"I020Csrf{Guid.NewGuid():N}"[..24];
        await SeedUserAsync(username, "correct horse battery staple");
        await using var factory = Factory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        var login = await client.PostAsJsonAsync("/v1/human-authentication/login",
            new HumanLoginRequest(username, "correct horse battery staple", "operator-console"));
        var loginCookies = login.Headers.GetValues("Set-Cookie").ToArray();
        var originalSessionCookie = CookieValue(loginCookies, "__Host-ExitPass-HumanSession");
        var csrfCookie = CookieValue(loginCookies, "__Host-ExitPass-Csrf");
        var csrf = login.Headers.GetValues("X-CSRF-Token").Single();
        client.DefaultRequestHeaders.Add("Cookie",
            $"__Host-ExitPass-HumanSession={originalSessionCookie}; __Host-ExitPass-Csrf={csrfCookie}");

        var rejected = await client.PostAsJsonAsync("/v1/human-authentication/session/continue", new HumanSessionContinueRequest());
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadFromJsonAsync<HumanAuthenticationResponse>())!.ErrorCode.Should().Be("CSRF_VALIDATION_FAILED");

        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
        var continued = await client.PostAsJsonAsync("/v1/human-authentication/session/continue", new HumanSessionContinueRequest());
        continued.StatusCode.Should().Be(HttpStatusCode.OK, await continued.Content.ReadAsStringAsync());
        var replacement = CookieValue(continued.Headers.GetValues("Set-Cookie"), "__Host-ExitPass-HumanSession");
        replacement.Should().NotBe(originalSessionCookie);
    }

    private CustomWebApplicationFactory Factory()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new CustomWebApplicationFactory().WithConfigurationOverrides(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MainDatabase"] = _database.ConnectionString,
            ["HumanAuthentication:Argon2Iterations"] = "1",
            ["HumanAuthentication:Argon2MemoryKiB"] = "19456",
            ["HumanAuthentication:Argon2Parallelism"] = "1",
            ["HumanAuthentication:TotpProtectionKeyBase64"] = key,
            ["HumanAuthentication:TotpProtectionKeyReference"] = "i020-api-test",
            ["HumanAuthentication:TotpProtectionKeyVersion"] = "1",
            ["HumanAuthentication:AllowedWebOrigins:0"] = "https://localhost"
        });
    }

    private async Task SeedUserAsync(string username, string password)
    {
        var options = Options.Create(new HumanAuthenticationOptions
        {
            Argon2Iterations = 1,
            Argon2MemoryKiB = 19456,
            Argon2Parallelism = 1,
            PasswordMinimumLength = 15
        });
        var material = await new Argon2idHumanPasswordHasher(options).HashAsync(password, CancellationToken.None);
        const string sql = """
            WITH new_user AS (
                INSERT INTO identity.users (user_id,username,display_name,user_type,user_status,effective_from,
                    created_by_service_identity_id,updated_by_service_identity_id)
                VALUES (gen_random_uuid(),@username,@username,'SITE_OPERATOR','ACTIVE',now()-interval '1 day',@service_id,@service_id)
                RETURNING user_id
            )
            INSERT INTO identity.local_credentials (local_credential_id,user_id,credential_status,password_verifier,
                verifier_salt,verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
                verifier_memory_kib,verifier_parallelism,activated_at,last_changed_at,
                created_by_service_identity_id,updated_by_service_identity_id)
            SELECT gen_random_uuid(),user_id,'ACTIVE',@verifier,@salt,@algorithm,@algorithm_version,@work_factor,
                @memory_kib,@parallelism,now(),now(),@service_id,@service_id FROM new_user;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        command.Parameters.AddWithValue("verifier", material.Verifier);
        command.Parameters.AddWithValue("salt", material.Salt);
        command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
        command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
        command.Parameters.AddWithValue("work_factor", material.Iterations);
        command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
        command.Parameters.AddWithValue("parallelism", material.Parallelism);
        await command.ExecuteNonQueryAsync();
    }

    private static string CookieValue(IEnumerable<string> setCookieHeaders, string name)
    {
        var prefix = name + "=";
        var header = setCookieHeaders.Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return header[prefix.Length..header.IndexOf(';')];
    }
}
