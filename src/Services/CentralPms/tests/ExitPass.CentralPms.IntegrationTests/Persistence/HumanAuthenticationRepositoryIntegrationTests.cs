using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Npgsql;
using OtpNet;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class HumanAuthenticationRepositoryIntegrationTests
{
    private static readonly Guid CentralPmsServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public HumanAuthenticationRepositoryIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database) =>
        _database = database;

    [Fact]
    public async Task Local_login_session_rotation_restart_and_live_scope_readback_use_canonical_state()
    {
        var options = TestOptions();
        var runtime = CreateRuntime(options);
        var user = await SeedUserAsync(runtime.Passwords, "I020Scope", "correct horse battery staple");
        var (siteId, siteGroupId) = await GrantSeededRoleAndScopesAsync(user);
        var context = Context();

        var login = await runtime.Service.LoginAsync("  i020scope  ", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);

        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.SiteReferences.Should().Contain(siteId);
        login.Response.Session.SiteGroupReferences.Should().Contain(siteGroupId);
        login.Response.Session.Permissions.Should().NotBeEmpty();
        login.Response.AptSessionToken.Should().BeNull();
        await AssertOnlySessionHashPersistedAsync(login.Credential!);

        var restarted = CreateRuntime(options);
        var rediscovered = await restarted.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, true, CancellationToken.None);
        rediscovered.Response.Authenticated.Should().BeTrue();

        var continued = await restarted.Service.ContinueSessionAsync(login.Credential.SerializedToken,
            context, CancellationToken.None);
        continued.Response.Authenticated.Should().BeTrue();
        continued.Credential!.SessionReference.Should().NotBe(login.Credential.SessionReference);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.human_sessions WHERE user_id=@id AND session_status='ACTIVE';", user)).Should().Be(1);
        (await ScalarAsync<string>("SELECT session_status::text FROM identity.human_sessions WHERE session_reference=@id;", login.Credential.SessionReference)).Should().Be("REVOKED");

        await ExecuteAsync("UPDATE identity.users SET user_status='SUSPENDED', suspended_at=now(), row_version=row_version+1 WHERE user_id=@user_id;", user);
        var suspended = await restarted.Service.ResolveSessionAsync(continued.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, false, CancellationToken.None);
        suspended.Response.Authenticated.Should().BeFalse();
        suspended.Response.ErrorCode.Should().Be("SESSION_REVOKED");
    }

    [Fact]
    public async Task Privileged_management_totp_is_required_and_one_time_step_cannot_replay()
    {
        var options = TestOptions();
        var runtime = CreateRuntime(options);
        var user = await SeedUserAsync(runtime.Passwords, "I020Privileged", "correct horse battery staple");
        await AssignPrivilegedRoleAsync(user);
        var authenticatorId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(20);
        try
        {
            var envelope = runtime.Protector.Protect(user, authenticatorId, secret);
            await SeedActiveAuthenticatorAsync(user, authenticatorId, envelope, runtime.Protector);
            var withoutTotp = await runtime.Service.LoginAsync("i020privileged", "correct horse battery staple",
                HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
            withoutTotp.Response.ErrorCode.Should().Be("TOTP_REQUIRED");

            var now = DateTimeOffset.UtcNow;
            var code = new Totp(secret, options.TotpStepSeconds, OtpHashMode.Sha1, options.TotpDigits).ComputeTotp(now.UtcDateTime);
            var accepted = await runtime.Service.LoginAsync("i020privileged", "correct horse battery staple",
                HumanSessionAudiences.ManagementPlatform, code, Context(), CancellationToken.None);
            accepted.Response.Authenticated.Should().BeTrue();
            accepted.Response.Session!.MfaRequired.Should().BeTrue();
            accepted.Response.Session.MfaSatisfied.Should().BeTrue();

            var replayed = await runtime.Service.LoginAsync("i020privileged", "correct horse battery staple",
                HumanSessionAudiences.ManagementPlatform, code, Context(), CancellationToken.None);
            replayed.Response.Authenticated.Should().BeFalse();
            replayed.Response.ErrorCode.Should().Be("TOTP_INVALID");
            (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.authentication_attempts WHERE user_id=@id AND attempt_type='TOTP';", user)).Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Fact]
    public async Task Password_reset_challenge_is_consumed_atomically_and_security_event_links_audit_event()
    {
        var options = TestOptions();
        var runtime = CreateRuntime(options);
        var user = await SeedUserAsync(runtime.Passwords, "I020Reset", "correct horse battery staple");
        var now = DateTimeOffset.UtcNow;
        var correlation = Guid.NewGuid();
        var challenge = await runtime.Repository.CreateCredentialChallengeAsync(user, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, correlation, CancellationToken.None);
        var context = Context(correlation);

        var reset = await runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret,
            "replacement horse battery staple", context, CancellationToken.None);
        reset.HttpStatusCode.Should().Be(200);

        var replay = await runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret,
            "another replacement battery staple", context, CancellationToken.None);
        replay.Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", challenge.Reference)).Should().Be("CONSUMED");

        var oldPassword = await runtime.Service.LoginAsync("i020reset", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        oldPassword.Response.Authenticated.Should().BeFalse();
        var newPassword = await runtime.Service.LoginAsync("i020reset", "replacement horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        newPassword.Response.Authenticated.Should().BeTrue();

        const string linkedEventSql = """
            SELECT count(*)::integer FROM audit.security_events se
            JOIN audit.audit_events ae ON ae.audit_event_id=se.audit_event_id
            WHERE se.correlation_id=@id AND se.security_event_type='CREDENTIAL_RESET'
              AND ae.event_type='CREDENTIAL_RESET';
            """;
        (await ScalarAsync<int>(linkedEventSql, correlation)).Should().Be(1);
    }

    [Fact]
    public async Task Password_failures_lock_and_throttle_then_expired_runtime_lockout_releases()
    {
        var options = TestOptions() with { MaximumFailures = 2, LockoutMinutes = 1 };
        var runtime = CreateRuntime(options);
        var user = await SeedUserAsync(runtime.Passwords, "I020Lockout", "correct horse battery staple");
        var context = Context();

        var first = await runtime.Service.LoginAsync("i020lockout", "wrong horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        var second = await runtime.Service.LoginAsync("i020lockout", "wrong horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        var throttled = await runtime.Service.LoginAsync("i020lockout", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);

        first.Response.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        second.Response.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        throttled.HttpStatusCode.Should().Be(429);
        (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", user)).Should().Be("LOCKED");

        await ExecuteAsync("UPDATE identity.users SET lockout_expires_at=now()-interval '1 second' WHERE user_id=@user_id;", user);
        var recovered = await runtime.Service.LoginAsync("i020lockout", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        recovered.Response.Authenticated.Should().BeTrue();
        (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", user)).Should().Be("ACTIVE");
    }

    [Fact]
    public async Task Apt_login_is_device_bound_audience_bound_and_does_not_require_totp()
    {
        var runtime = CreateRuntime(TestOptions());
        await SeedUserAsync(runtime.Passwords, "I020Apt", "correct horse battery staple");
        var (deviceId, siteId) = await SeedAptDeviceAsync();
        var context = Context() with { DeviceServiceIdentityId = deviceId, SiteId = siteId };

        var login = await runtime.Service.LoginAsync("i020apt", "correct horse battery staple",
            HumanSessionAudiences.Apt, null, context, CancellationToken.None);
        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.MfaRequired.Should().BeFalse();
        login.Response.AptSessionToken.Should().NotBeNullOrWhiteSpace();

        var wrongDevice = await runtime.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.Apt, Guid.NewGuid(), context, false, CancellationToken.None);
        wrongDevice.Response.ErrorCode.Should().Be("SESSION_BINDING_MISMATCH");
        var wrongAudience = await runtime.Service.ResolveSessionAsync(login.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, deviceId, context, false, CancellationToken.None);
        wrongAudience.Response.ErrorCode.Should().Be("SESSION_BINDING_MISMATCH");
    }

    [Fact]
    public async Task Session_expiry_credential_rotation_and_live_authorization_epoch_fail_closed()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var runtime = CreateRuntime(TestOptions(), clock);
        var user = await SeedUserAsync(runtime.Passwords, "I020Epoch", "correct horse battery staple");
        await GrantSeededRoleAndScopesAsync(user);
        var context = Context();
        var login = await runtime.Service.LoginAsync("i020epoch", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);

        await ExecuteAsync("UPDATE identity.users SET authorization_epoch=authorization_epoch+1 WHERE user_id=@user_id;", user);
        var liveAuthorization = await runtime.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, false, CancellationToken.None);
        liveAuthorization.Response.Authenticated.Should().BeTrue();
        liveAuthorization.Response.Session!.Permissions.Should().NotBeEmpty();

        await ExecuteAsync("UPDATE identity.users SET credential_version=credential_version+1 WHERE user_id=@user_id;", user);
        var invalidated = await runtime.Service.ResolveSessionAsync(login.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, false, CancellationToken.None);
        invalidated.Response.ErrorCode.Should().Be("SESSION_REVOKED");

        var replacement = await runtime.Service.LoginAsync("i020epoch", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(TestOptions().WebIdleMinutes + 1));
        var expired = await runtime.Service.ResolveSessionAsync(replacement.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, false, CancellationToken.None);
        expired.Response.ErrorCode.Should().Be("SESSION_EXPIRED");
    }

    [Fact]
    public async Task Privileged_totp_enrollment_confirmation_and_governed_reset_keep_secret_protected()
    {
        var runtime = CreateRuntime(TestOptions());
        var user = await SeedUserAsync(runtime.Passwords, "I020Enroll", "correct horse battery staple");
        await AssignPrivilegedRoleAsync(user);
        var context = Context();
        var login = await runtime.Service.LoginAsync("i020enroll", "correct horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, context, CancellationToken.None);
        login.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.MfaEnrollmentRequired);

        var enrollment = await runtime.Service.BeginTotpEnrollmentAsync(login.Credential!.SerializedToken,
            context, CancellationToken.None);
        enrollment.Response.SharedSecret.Should().NotBeNullOrWhiteSpace();
        enrollment.Response.ProvisioningUri.Should().NotBeNullOrWhiteSpace();
        var secret = Base32Encoding.ToBytes(enrollment.Response.SharedSecret!);
        try
        {
            var code = new Totp(secret, 30, OtpHashMode.Sha1, 6).ComputeTotp(DateTime.UtcNow);
            var confirmed = await runtime.Service.ConfirmTotpEnrollmentAsync(login.Credential.SerializedToken,
                code, context, CancellationToken.None);
            confirmed.Response.Outcome.Should().Be("TOTP_CONFIRMED_REAUTHENTICATION_REQUIRED");
            confirmed.Response.SharedSecret.Should().BeNull();
            confirmed.Response.ProvisioningUri.Should().BeNull();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        var envelope = await ScalarAsync<byte[]>("SELECT protected_secret_envelope FROM identity.user_mfa_authenticators WHERE user_id=@id AND authenticator_status='ACTIVE';", user);
        envelope.Should().NotBeEmpty();
        await runtime.Service.ResetTotpAsync(user, user, "USER_REQUEST", Guid.NewGuid(), CancellationToken.None);
        (await ScalarAsync<string>("SELECT authenticator_status::text FROM identity.user_mfa_authenticators WHERE user_id=@id ORDER BY created_at DESC LIMIT 1;", user)).Should().Be("RESET_REQUIRED");
    }

    private Runtime CreateRuntime(HumanAuthenticationOptions options, TimeProvider? timeProvider = null)
    {
        var configured = Options.Create(options);
        var tokens = new HumanSessionTokenService();
        var repository = new PostgresHumanAuthenticationRepository(_database.ConnectionString, tokens);
        var passwords = new Argon2idHumanPasswordHasher(configured);
        var protector = new AesGcmTotpSecretProtector(configured);
        var service = new HumanAuthenticationService(repository, passwords, new TotpProvider(configured),
            protector, tokens, new DisabledCredentialChallengeDelivery(), timeProvider ?? TimeProvider.System, configured);
        return new Runtime(repository, service, passwords, protector);
    }

    private async Task<Guid> SeedUserAsync(IHumanPasswordHasher passwords, string username, string password)
    {
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var material = await passwords.HashAsync(password, CancellationToken.None);
        const string sql = """
            INSERT INTO identity.users (user_id,username,display_name,user_type,user_status,effective_from,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_id,@username,@username,'SITE_OPERATOR','ACTIVE',now()-interval '1 day',@service_id,@service_id);
            INSERT INTO identity.local_credentials (local_credential_id,user_id,credential_status,password_verifier,
                verifier_salt,verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
                verifier_memory_kib,verifier_parallelism,activated_at,last_changed_at,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@credential_id,@user_id,'ACTIVE',@verifier,@salt,@algorithm,@algorithm_version,@work_factor,
                @memory_kib,@parallelism,now(),now(),@service_id,@service_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("credential_id", credentialId);
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
        return userId;
    }

    private async Task<(Guid SiteId, Guid SiteGroupId)> GrantSeededRoleAndScopesAsync(Guid userId)
    {
        var roleId = await ScalarAsync<Guid>("SELECT role_id FROM identity.roles WHERE role_status='ACTIVE' ORDER BY role_code LIMIT 1;");
        var siteId = await ScalarAsync<Guid>("SELECT site_id FROM sites.sites WHERE site_status='ACTIVE' ORDER BY site_code LIMIT 1;");
        var siteGroupId = await ScalarAsync<Guid>("SELECT site_group_id FROM sites.site_groups WHERE site_group_status='ACTIVE' ORDER BY site_group_code LIMIT 1;");
        var userRoleId = Guid.NewGuid();
        const string sql = """
            INSERT INTO identity.user_roles (user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_by_service_identity_id,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_role_id,@user_id,@role_id,'ACTIVE','I020_TEST',@service_id,now()-interval '1 day',@service_id,@service_id);
            INSERT INTO identity.user_role_scope_grants (user_role_scope_grant_id,user_role_id,scope_type,site_id,
                grant_status,grant_reason_code,effective_from,granted_by_service_identity_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@user_role_id,'SITE',@site_id,'ACTIVE','I020_TEST',now()-interval '1 day',@service_id,@service_id,@service_id);
            INSERT INTO identity.user_role_scope_grants (user_role_scope_grant_id,user_role_id,scope_type,site_group_id,
                grant_status,grant_reason_code,effective_from,granted_by_service_identity_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@user_role_id,'SITE_GROUP',@site_group_id,'ACTIVE','I020_TEST',now()-interval '1 day',@service_id,@service_id,@service_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_role_id", userRoleId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("role_id", roleId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        await command.ExecuteNonQueryAsync();
        return (siteId, siteGroupId);
    }

    private async Task AssignPrivilegedRoleAsync(Guid userId)
    {
        var roleId = await ScalarAsync<Guid>("SELECT role_id FROM identity.roles WHERE role_status='ACTIVE' AND is_privileged ORDER BY role_code LIMIT 1;");
        const string sql = """
            INSERT INTO identity.user_roles (user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_by_service_identity_id,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@user_id,@role_id,'ACTIVE','I020_TEST',@service_id,now()-interval '1 day',@service_id,@service_id);
            """;
        await ExecuteAsync(sql, userId, ("role_id", roleId), ("service_id", CentralPmsServiceIdentityId));
    }

    private async Task SeedActiveAuthenticatorAsync(Guid userId, Guid authenticatorId, byte[] envelope, ITotpSecretProtector protector)
    {
        const string sql = """
            INSERT INTO identity.user_mfa_authenticators (user_mfa_authenticator_id,user_id,authenticator_type,
                authenticator_status,protected_secret_envelope,protection_key_reference,protection_key_version,
                envelope_format_version,enrollment_started_at,activated_at,created_by_service_identity_id,
                updated_by_service_identity_id)
            VALUES (@id,@user_id,'TOTP','ACTIVE',@envelope,@key_reference,@key_version,@format_version,
                now()-interval '1 minute',now(),@service_id,@service_id);
            """;
        await ExecuteAsync(sql, userId, ("id", authenticatorId), ("envelope", envelope),
            ("key_reference", protector.KeyReference), ("key_version", protector.KeyVersion),
            ("format_version", protector.EnvelopeFormatVersion), ("service_id", CentralPmsServiceIdentityId));
    }

    private async Task<(Guid DeviceId, Guid SiteId)> SeedAptDeviceAsync()
    {
        var deviceId = Guid.NewGuid();
        var siteId = await ScalarAsync<Guid>("SELECT site_id FROM sites.sites WHERE site_status='ACTIVE' ORDER BY site_code LIMIT 1;");
        const string sql = """
            INSERT INTO identity.service_identities (service_identity_id,service_identity_code,service_identity_name,
                identity_type,identity_status,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@device_id,@code,@code,'DEVICE','ACTIVE',now()-interval '1 day',@service_id,@service_id);
            INSERT INTO sites.device_assignments (device_assignment_id,site_id,service_identity_id,assignment_type,
                assignment_status,assignment_reason_code,assigned_by_service_identity_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@site_id,@device_id,'PAYMENT_DEVICE','ACTIVE','I020_TEST',@service_id,@service_id,@service_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("code", $"I020_APT_{deviceId:N}"[..32]);
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        await command.ExecuteNonQueryAsync();
        return (deviceId, siteId);
    }

    private async Task AssertOnlySessionHashPersistedAsync(SessionCredential credential)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT session_secret_hash FROM identity.human_sessions WHERE session_reference=@id;", connection);
        command.Parameters.AddWithValue("id", credential.SessionReference);
        var hash = (string)(await command.ExecuteScalarAsync() ?? string.Empty);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        hash.Should().NotContain(credential.Secret);
        hash.Should().NotContain(credential.SerializedToken);
    }

    private async Task ExecuteAsync(string sql, Guid userId, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private Task ExecuteAsync(string sql, Guid id) => ExecuteAsync(sql, id, []);

    private async Task<T> ScalarAsync<T>(string sql, Guid? id = null)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private static HumanAuthenticationContext Context(Guid? correlation = null)
    {
        var value = correlation ?? Guid.NewGuid();
        var privacyHash = value.ToString("N") + value.ToString("N");
        return new HumanAuthenticationContext(value, CentralPmsServiceIdentityId, privacyHash, privacyHash, null, null);
    }

    private static HumanAuthenticationOptions TestOptions() => new()
    {
        Argon2Iterations = 1,
        Argon2MemoryKiB = 19456,
        Argon2Parallelism = 1,
        Argon2HashBytes = 32,
        PasswordMinimumLength = 15,
        TotpAllowedPreviousSteps = 0,
        TotpAllowedFutureSteps = 0,
        TotpProtectionKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        TotpProtectionKeyReference = "i020-test-key",
        TotpProtectionKeyVersion = "1"
    };

    private sealed record Runtime(
        PostgresHumanAuthenticationRepository Repository,
        HumanAuthenticationService Service,
        IHumanPasswordHasher Passwords,
        ITotpSecretProtector Protector);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
