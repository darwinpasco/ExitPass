using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using OtpNet;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class HumanAuthenticationRepositoryIntegrationTests
{
    private const string Password = "correct horse battery staple";
    private const string ReplacementPassword = "newpass8";
    private static readonly Guid CentralPmsServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public HumanAuthenticationRepositoryIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database) =>
        _database = database;

    [Fact]
    public async Task Site_operator_normal_login_succeeds_without_totp()
    {
        var runtime = CreateRuntime(TestOptions());
        var seed = await SeedCurrentUserAsync(runtime, "I020SiteOperator", ApprovedIdentityRoleCatalog.SiteOperator);

        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);

        login.Response.Authenticated.Should().BeTrue();
        login.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.Authenticated);
        login.Response.Session!.MfaRequired.Should().BeFalse();
        login.Response.Session.MfaSatisfied.Should().BeFalse();
        login.Response.Session.SiteReferences.Should().ContainSingle().Which.Should().Be(seed.SiteId);
        login.Response.Session.HasGlobalScope.Should().BeFalse();
    }

    [Fact]
    public async Task Operations_supervisor_operator_login_succeeds_without_totp()
    {
        var runtime = CreateRuntime(TestOptions());
        var seed = await SeedCurrentUserAsync(runtime, "I020OpsSupervisor", ApprovedIdentityRoleCatalog.OperationsSupervisor);

        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);

        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.MfaRequired.Should().BeFalse();
        login.Response.Session.SiteReferences.Should().ContainSingle().Which.Should().Be(seed.SiteId);
        login.Response.Session.HasGlobalScope.Should().BeFalse();
        login.Response.Session.Permissions.Should().NotContain([
            "statutory-discounts.review.queue.read",
            "statutory-discounts.review.detail.read",
            "statutory-discounts.evidence.review.view",
            "statutory-discounts.decision.review",
            "statutory-discounts.decision.approve",
            "statutory-discounts.decision.reject"
        ]);
    }

    [Fact]
    public async Task Apt_login_is_device_bound_audience_bound_and_does_not_require_totp()
    {
        var runtime = CreateRuntime(TestOptions());
        var seed = await SeedCurrentUserAsync(runtime, "I020Apt", ApprovedIdentityRoleCatalog.AptCashierOperator);
        var deviceId = await SeedAptDeviceAsync(seed.SiteId);
        var context = Context() with { DeviceServiceIdentityId = deviceId, SiteId = seed.SiteId };

        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.Apt, null, context, CancellationToken.None);

        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.MfaRequired.Should().BeFalse();
        login.Response.Session.Permissions.Should().Contain("apt.cashier.operate");
        login.Response.AptSessionToken.Should().NotBeNullOrWhiteSpace();

        var wrongDevice = await runtime.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.Apt, Guid.NewGuid(), context, false, CancellationToken.None);
        wrongDevice.Response.ErrorCode.Should().Be("SESSION_BINDING_MISMATCH");
        var wrongAudience = await runtime.Service.ResolveSessionAsync(login.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, deviceId, context, false, CancellationToken.None);
        wrongAudience.Response.ErrorCode.Should().Be("SESSION_BINDING_MISMATCH");
    }

    [Fact]
    public async Task Management_platform_login_requires_preprovisioned_totp_and_rejects_replay()
    {
        var options = TestOptions();
        var runtime = CreateRuntime(options);
        var seed = await SeedCurrentUserAsync(runtime, "I020Administrator", ApprovedIdentityRoleCatalog.SystemAdministrator);

        var withoutTotp = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
        withoutTotp.Response.ErrorCode.Should().Be("TOTP_REQUIRED");

        var code = TotpCode(seed.TotpSecret, options, DateTimeOffset.UtcNow);
        var accepted = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.ManagementPlatform, code, Context(), CancellationToken.None);
        accepted.Response.Authenticated.Should().BeTrue();
        accepted.Response.Session!.MfaRequired.Should().BeTrue();
        accepted.Response.Session.MfaSatisfied.Should().BeTrue();

        var replayed = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.ManagementPlatform, code, Context(), CancellationToken.None);
        replayed.Response.ErrorCode.Should().Be("TOTP_INVALID");
    }

    [Fact]
    public async Task Wrong_audience_is_denied_and_unknown_role_policy_fails_closed()
    {
        var runtime = CreateRuntime(TestOptions());
        var siteOperator = await SeedCurrentUserAsync(runtime, "I020WrongAudience", ApprovedIdentityRoleCatalog.SiteOperator);

        var denied = await runtime.Service.LoginAsync(siteOperator.Username, Password,
            HumanSessionAudiences.ManagementPlatform, TotpCode(siteOperator.TotpSecret, TestOptions(), DateTimeOffset.UtcNow),
            Context(), CancellationToken.None);
        denied.Response.Authenticated.Should().BeFalse();
        denied.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_DENIED");

        var unknown = await SeedCurrentUserAsync(runtime, "I020UnknownRole", "I020_UNKNOWN_ROLE", allowUnknownRole: true);
        var failClosed = await runtime.Service.LoginAsync(unknown.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        failClosed.Response.Authenticated.Should().BeFalse();
        failClosed.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_POLICY_UNKNOWN");
    }

    [Fact]
    public async Task First_login_returns_restricted_password_change_required_session_and_change_revokes_it()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = TestOptions();
        var runtime = CreateRuntime(options, clock);
        var seed = await SeedBootstrapUserAsync(runtime, "I020FirstLogin", ApprovedIdentityRoleCatalog.SiteOperator,
            clock.GetUtcNow());

        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        login.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.PasswordChangeRequired);
        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.PasswordChangeRequired.Should().BeTrue();
        login.Response.Session.Permissions.Should().BeEmpty();
        login.Response.Session.SiteReferences.Should().BeEmpty();

        var changed = await runtime.Service.ChangePasswordAsync(login.Credential!.SerializedToken, Password,
            ReplacementPassword, TotpCode(seed.TotpSecret, options, clock.GetUtcNow()), Context(), CancellationToken.None);
        changed.HttpStatusCode.Should().Be(200);
        changed.Response.Outcome.Should().Be("PASSWORD_CHANGED");
        changed.Response.Authenticated.Should().BeFalse();
        changed.Credential.Should().BeNull();

        var revoked = await runtime.Service.ResolveSessionAsync(login.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, Context(), false, CancellationToken.None);
        revoked.Response.Authenticated.Should().BeFalse();

        var newLogin = await runtime.Service.LoginAsync(seed.Username, ReplacementPassword,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        newLogin.Response.Authenticated.Should().BeTrue();
        newLogin.Response.Session!.PasswordChangeRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Temporary_password_is_expired_at_exactly_seventy_two_hours()
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var clock = new MutableTimeProvider(issuedAt);
        var runtime = CreateRuntime(TestOptions(), clock);
        var seed = await SeedBootstrapUserAsync(runtime, "I020ExactExpiry", ApprovedIdentityRoleCatalog.SiteOperator, issuedAt);

        (await ScalarAsync<decimal>(
            "SELECT EXTRACT(EPOCH FROM (temporary_password_expires_at-created_at)) FROM identity.local_credentials WHERE user_id=@id;",
            seed.UserId)).Should().Be(HumanAuthenticationOptions.RequiredTemporaryPasswordHours * 60 * 60);

        clock.Advance(TimeSpan.FromHours(HumanAuthenticationOptions.RequiredTemporaryPasswordHours));
        var expired = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        expired.Response.Authenticated.Should().BeFalse();
        expired.Response.ErrorCode.Should().Be("TEMPORARY_PASSWORD_EXPIRED");
    }

    [Fact]
    public async Task Active_password_reset_requires_totp_rotates_password_and_revokes_sessions()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = TestOptions();
        var runtime = CreateRuntime(options, clock);
        var seed = await SeedCurrentUserAsync(runtime, "I020ActiveReset", ApprovedIdentityRoleCatalog.SiteOperator);
        var session = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);

        var missing = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, null, "", ReplacementPassword,
            Context(), CancellationToken.None);
        missing.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
        var wrong = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, null, "000000", ReplacementPassword,
            Context(), CancellationToken.None);
        wrong.Response.ErrorCode.Should().Be("TOTP_INVALID");

        var reset = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, null,
            TotpCode(seed.TotpSecret, options, clock.GetUtcNow()), ReplacementPassword, Context(), CancellationToken.None);
        reset.HttpStatusCode.Should().Be(200);
        reset.Response.Outcome.Should().Be("PASSWORD_RESET_COMPLETED");
        reset.Response.Authenticated.Should().BeFalse();

        var revoked = await runtime.Service.ResolveSessionAsync(session.Credential!.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, Context(), false, CancellationToken.None);
        revoked.Response.Authenticated.Should().BeFalse();
        (await runtime.Service.LoginAsync(seed.Username, Password, HumanSessionAudiences.OperatorConsole,
            null, Context(), CancellationToken.None)).Response.Authenticated.Should().BeFalse();
        (await runtime.Service.LoginAsync(seed.Username, ReplacementPassword, HumanSessionAudiences.OperatorConsole,
            null, Context(), CancellationToken.None)).Response.Authenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Expired_bootstrap_reset_requires_matching_temporary_password_and_totp()
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var clock = new MutableTimeProvider(issuedAt.AddHours(HumanAuthenticationOptions.RequiredTemporaryPasswordHours));
        var options = TestOptions();
        var runtime = CreateRuntime(options, clock);
        var seed = await SeedBootstrapUserAsync(runtime, "I020ExpiredReset", ApprovedIdentityRoleCatalog.SiteOperator, issuedAt);
        var code = TotpCode(seed.TotpSecret, options, clock.GetUtcNow());

        var missingPassword = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, null, code,
            ReplacementPassword, Context(), CancellationToken.None);
        missingPassword.Response.ErrorCode.Should().Be("EXPIRED_TEMPORARY_PASSWORD_REQUIRED");
        var wrongPassword = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, "wrong temporary password", code,
            ReplacementPassword, Context(), CancellationToken.None);
        wrongPassword.Response.ErrorCode.Should().Be("EXPIRED_TEMPORARY_PASSWORD_REQUIRED");
        var wrongTotp = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, Password, "000000",
            ReplacementPassword, Context(), CancellationToken.None);
        wrongTotp.Response.ErrorCode.Should().Be("TOTP_INVALID");

        var reset = await runtime.Service.ResetPasswordWithTotpAsync(seed.Username, Password, code,
            ReplacementPassword, Context(), CancellationToken.None);
        reset.HttpStatusCode.Should().Be(200);
        reset.Response.Outcome.Should().Be("PASSWORD_RESET_COMPLETED");

        (await runtime.Service.LoginAsync(seed.Username, Password, HumanSessionAudiences.OperatorConsole,
            null, Context(), CancellationToken.None)).Response.Authenticated.Should().BeFalse();
        (await runtime.Service.LoginAsync(seed.Username, ReplacementPassword, HumanSessionAudiences.OperatorConsole,
            null, Context(), CancellationToken.None)).Response.Authenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Voluntary_password_change_requires_totp_for_operational_user()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = TestOptions();
        var runtime = CreateRuntime(options, clock);
        var seed = await SeedCurrentUserAsync(runtime, "I020VoluntaryChange", ApprovedIdentityRoleCatalog.SiteOperator);
        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);

        var missing = await runtime.Service.ChangePasswordAsync(login.Credential!.SerializedToken, Password,
            ReplacementPassword, null, Context(), CancellationToken.None);
        missing.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
        var wrong = await runtime.Service.ChangePasswordAsync(login.Credential.SerializedToken, Password,
            ReplacementPassword, "000000", Context(), CancellationToken.None);
        wrong.Response.ErrorCode.Should().Be("TOTP_INVALID");
        var changed = await runtime.Service.ChangePasswordAsync(login.Credential.SerializedToken, Password,
            ReplacementPassword, TotpCode(seed.TotpSecret, options, clock.GetUtcNow()), Context(), CancellationToken.None);
        changed.HttpStatusCode.Should().Be(200);
        changed.Response.Outcome.Should().Be("PASSWORD_CHANGED");
    }

    [Fact]
    public async Task Superseded_challenge_flows_are_disabled_and_pending_activation_has_no_authority()
    {
        var runtime = CreateRuntime(TestOptions());
        var activation = await runtime.Service.ActivateAsync(Guid.NewGuid(), "legacy-secret", ReplacementPassword,
            Context(), CancellationToken.None);
        activation.HttpStatusCode.Should().Be(410);
        activation.Response.ErrorCode.Should().Be("TEMPORARY_PASSWORD_LOGIN_REQUIRED");

        var reset = await runtime.Service.ResetPasswordAsync(Guid.NewGuid(), "legacy-secret", ReplacementPassword,
            Context(), CancellationToken.None);
        reset.HttpStatusCode.Should().Be(410);
        reset.Response.ErrorCode.Should().Be("TOTP_RESET_REQUIRED");

        var pending = await SeedLegacyPendingActivationUserAsync(runtime, "I020LegacyPending");
        var login = await runtime.Service.LoginAsync(pending.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        login.Response.Authenticated.Should().BeFalse();
        login.Response.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Provisioned_totp_secret_is_encrypted_and_available_before_first_login()
    {
        var runtime = CreateRuntime(TestOptions());
        var seed = await SeedBootstrapUserAsync(runtime, "I020ProtectedTotp", ApprovedIdentityRoleCatalog.SystemAdministrator,
            DateTimeOffset.UtcNow);

        var stored = await ScalarAsync<byte[]>(
            "SELECT protected_secret_envelope FROM identity.user_mfa_authenticators WHERE user_id=@id AND authenticator_status='ACTIVE';",
            seed.UserId);
        stored.Should().NotBeEmpty();
        stored.Should().NotEqual(seed.TotpSecret);
        (await ScalarAsync<int>(
            "SELECT count(*)::integer FROM identity.user_mfa_authenticators WHERE user_id=@id AND authenticator_status='PENDING_ENROLLMENT';",
            seed.UserId)).Should().Be(0);

        var missing = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
        missing.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
    }

    [Fact]
    public async Task Password_failures_lock_and_expired_runtime_lockout_releases()
    {
        var options = TestOptions() with { MaximumFailures = 2, LockoutMinutes = 1 };
        var runtime = CreateRuntime(options);
        var seed = await SeedCurrentUserAsync(runtime, "I020Lockout", ApprovedIdentityRoleCatalog.SiteOperator);
        var context = Context();

        (await runtime.Service.LoginAsync(seed.Username, "wrong horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None)).Response.ErrorCode
            .Should().Be("INVALID_CREDENTIALS");
        (await runtime.Service.LoginAsync(seed.Username, "wrong horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None)).Response.ErrorCode
            .Should().Be("INVALID_CREDENTIALS");
        (await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None)).HttpStatusCode.Should().Be(429);

        await ExecuteAsync("UPDATE identity.users SET lockout_expires_at=now()-interval '1 second' WHERE user_id=@user_id;",
            ("user_id", seed.UserId));
        var recovered = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        recovered.Response.Authenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Session_rotation_and_live_authorization_epoch_fail_closed()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var runtime = CreateRuntime(TestOptions(), clock);
        var seed = await SeedCurrentUserAsync(runtime, "I020Epoch", ApprovedIdentityRoleCatalog.SiteOperator);
        var context = Context();
        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);

        var continued = await runtime.Service.ContinueSessionAsync(login.Credential!.SerializedToken,
            context, CancellationToken.None);
        continued.Response.Authenticated.Should().BeTrue();
        continued.Credential!.SessionReference.Should().NotBe(login.Credential.SessionReference);

        await ExecuteAsync("UPDATE identity.users SET authorization_epoch=authorization_epoch+1 WHERE user_id=@user_id;",
            ("user_id", seed.UserId));
        var invalidated = await runtime.Service.ResolveSessionAsync(continued.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, false, CancellationToken.None);
        invalidated.Response.ErrorCode.Should().Be("SESSION_REVOKED");
    }

    [Fact]
    public async Task Web_session_uses_sliding_idle_and_fixed_absolute_expiry()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = TestOptions();
        var runtime = CreateRuntime(options, clock);
        var seed = await SeedCurrentUserAsync(runtime, "I020Sliding", ApprovedIdentityRoleCatalog.SiteOperator);
        var context = Context();
        var login = await runtime.Service.LoginAsync(seed.Username, Password,
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        var initial = login.Response.Session!;
        initial.IdleExpiresAt.Should().Be(initial.AuthenticatedAt.AddMinutes(options.WebIdleMinutes));
        initial.AbsoluteExpiresAt.Should().Be(initial.AuthenticatedAt.AddHours(options.WebAbsoluteHours));

        clock.Advance(TimeSpan.FromMinutes(16));
        var active = await runtime.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, true, CancellationToken.None);
        active.Response.Session!.IdleExpiresAt.Should().Be(clock.GetUtcNow().AddMinutes(options.WebIdleMinutes));
        active.Response.Session.AbsoluteExpiresAt.Should().Be(initial.AbsoluteExpiresAt);

        clock.Advance(TimeSpan.FromHours(options.WebAbsoluteHours));
        var expired = await runtime.Service.ResolveSessionAsync(login.Credential.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, context, false, CancellationToken.None);
        expired.Response.ErrorCode.Should().Be("SESSION_EXPIRED");
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
        return new Runtime(repository, service, passwords, protector, options, timeProvider ?? TimeProvider.System);
    }

    private async Task<UserSeed> SeedCurrentUserAsync(
        Runtime runtime,
        string username,
        string roleCode,
        bool allowUnknownRole = false) =>
        await SeedUserAsync(runtime, username, roleCode, "ACTIVE", null, runtime.Clock.GetUtcNow(), allowUnknownRole);

    private async Task<UserSeed> SeedBootstrapUserAsync(
        Runtime runtime,
        string username,
        string roleCode,
        DateTimeOffset issuedAt) =>
        await SeedUserAsync(runtime, username, roleCode, "CHANGE_REQUIRED",
            issuedAt.AddHours(HumanAuthenticationOptions.RequiredTemporaryPasswordHours), issuedAt, false);

    private async Task<UserSeed> SeedLegacyPendingActivationUserAsync(Runtime runtime, string username) =>
        await SeedUserAsync(runtime, username, ApprovedIdentityRoleCatalog.SiteOperator, "PENDING_ACTIVATION",
            null, DateTimeOffset.UtcNow, false, "INVITED");

    private async Task<UserSeed> SeedUserAsync(
        Runtime runtime,
        string username,
        string roleCode,
        string credentialStatus,
        DateTimeOffset? temporaryPasswordExpiresAt,
        DateTimeOffset issuedAt,
        bool allowUnknownRole,
        string userStatus = "ACTIVE")
    {
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var authenticatorId = Guid.NewGuid();
        var userRoleId = Guid.NewGuid();
        var material = await runtime.Passwords.HashAsync(Password, CancellationToken.None);
        var secret = RandomNumberGenerator.GetBytes(20);
        var envelope = runtime.Protector.Protect(userId, authenticatorId, secret);
        var (siteId, siteGroupId) = await ActivateCanonicalPitxLevel3Async();

        if (allowUnknownRole)
        {
            await ExecuteAsync("""
                INSERT INTO identity.roles (role_id,role_code,role_name,role_type,role_status,is_privileged,
                    requires_elevated_approval,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
                VALUES (gen_random_uuid(),@role_code,@role_code,'OTHER','ACTIVE',false,false,
                    now()-interval '1 day',@service_id,@service_id);
                """, ("role_code", roleCode), ("service_id", CentralPmsServiceIdentityId));
        }

        const string sql = """
            INSERT INTO identity.users (user_id,username,display_name,user_type,user_status,effective_from,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_id,@username,@username,'SITE_OPERATOR',@user_status::identity.user_status_enum,
                now()-interval '1 day',@service_id,@service_id);

            INSERT INTO identity.local_credentials (local_credential_id,user_id,credential_status,password_verifier,
                verifier_salt,verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
                verifier_memory_kib,verifier_parallelism,temporary_password_expires_at,activated_at,last_changed_at,
                created_by_service_identity_id,updated_by_service_identity_id,created_at,updated_at)
            VALUES (@credential_id,@user_id,@credential_status::identity.local_credential_status_enum,@verifier,@salt,
                @algorithm,@algorithm_version,@work_factor,@memory_kib,@parallelism,@expires_at,now(),now(),
                @service_id,@service_id,@issued_at,@issued_at);

            INSERT INTO identity.user_mfa_authenticators (user_mfa_authenticator_id,user_id,authenticator_type,
                authenticator_status,protected_secret_envelope,protection_key_reference,protection_key_version,
                envelope_format_version,enrollment_started_at,activated_at,created_by_service_identity_id,
                updated_by_service_identity_id)
            VALUES (@authenticator_id,@user_id,'TOTP','ACTIVE',@envelope,@key_reference,@key_version,@format_version,
                now(),now(),@service_id,@service_id);

            INSERT INTO identity.user_roles (user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_by_service_identity_id,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            SELECT @user_role_id,@user_id,role_id,'ACTIVE','I020_CURRENT',@service_id,
                now()-interval '1 day',@service_id,@service_id
            FROM identity.roles WHERE role_code=@role_code AND role_status='ACTIVE';
            """;
        await ExecuteAsync(sql,
            ("user_id", userId), ("credential_id", credentialId), ("authenticator_id", authenticatorId),
            ("user_role_id", userRoleId), ("username", username), ("role_code", roleCode),
            ("user_status", userStatus), ("credential_status", credentialStatus),
            ("verifier", material.Verifier), ("salt", material.Salt), ("algorithm", material.AlgorithmCode),
            ("algorithm_version", material.AlgorithmVersion), ("work_factor", material.Iterations),
            ("memory_kib", material.MemoryKiB), ("parallelism", material.Parallelism),
            ("expires_at", (object?)temporaryPasswordExpiresAt ?? DBNull.Value), ("issued_at", issuedAt),
            ("envelope", envelope), ("key_reference", runtime.Protector.KeyReference),
            ("key_version", runtime.Protector.KeyVersion), ("format_version", runtime.Protector.EnvelopeFormatVersion),
            ("service_id", CentralPmsServiceIdentityId));

        var scopeType = roleCode == ApprovedIdentityRoleCatalog.SystemAdministrator
            ? ApprovedIdentityRoleCatalog.GlobalScope
            : ApprovedIdentityRoleCatalog.SiteScope;
        await ExecuteAsync("""
            INSERT INTO identity.user_role_scope_grants (user_role_scope_grant_id,user_role_id,scope_type,site_id,
                grant_status,grant_reason_code,effective_from,granted_by_service_identity_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@user_role_id,@scope_type::identity.authorization_scope_type_enum,@site_id,
                'ACTIVE','I020_CURRENT',now()-interval '1 day',@service_id,@service_id,@service_id);
            """, ("user_role_id", userRoleId), ("scope_type", scopeType),
            ("site_id", scopeType == ApprovedIdentityRoleCatalog.SiteScope ? siteId : DBNull.Value),
            ("service_id", CentralPmsServiceIdentityId));

        return new UserSeed(userId, username, roleCode, userRoleId, siteId, siteGroupId, secret);
    }

    private async Task<(Guid SiteId, Guid SiteGroupId)> ActivateCanonicalPitxLevel3Async()
    {
        var siteGroupId = Guid.Parse("a6dbadf6-68b5-5bed-a7e0-a75faee70841");
        var siteId = Guid.Parse("2d1dcdf8-f563-537c-8542-0bde7cc9da97");
        await ExecuteAsync("""
            UPDATE sites.site_groups SET site_group_status='ACTIVE' WHERE site_group_id=@site_group_id;
            UPDATE sites.sites SET site_status='ACTIVE' WHERE site_id=@site_id AND site_group_id=@site_group_id;
            """, ("site_group_id", siteGroupId), ("site_id", siteId));
        return (siteId, siteGroupId);
    }

    private async Task<Guid> SeedAptDeviceAsync(Guid siteId)
    {
        var deviceId = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT INTO identity.service_identities (service_identity_id,service_identity_code,service_identity_name,
                identity_type,identity_status,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@device_id,@code,@code,'DEVICE','ACTIVE',now()-interval '1 day',@service_id,@service_id);
            INSERT INTO sites.device_assignments (device_assignment_id,site_id,service_identity_id,assignment_type,
                assignment_status,assignment_reason_code,assigned_by_service_identity_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (gen_random_uuid(),@site_id,@device_id,'PAYMENT_DEVICE','ACTIVE','I020_CURRENT',
                @service_id,@service_id,@service_id);
            """, ("device_id", deviceId), ("code", $"I020_APT_{deviceId:N}"[..32]),
            ("site_id", siteId), ("service_id", CentralPmsServiceIdentityId));
        return deviceId;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected scalar value."));
    }

    private static string TotpCode(byte[] secret, HumanAuthenticationOptions options, DateTimeOffset now) =>
        new Totp(secret, options.TotpStepSeconds, OtpHashMode.Sha1, options.TotpDigits).ComputeTotp(now.UtcDateTime);

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
        PasswordMinimumLength = 8,
        TotpAllowedPreviousSteps = 0,
        TotpAllowedFutureSteps = 0,
        TotpProtectionKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        TotpProtectionKeyReference = "i020-current-key",
        TotpProtectionKeyVersion = "1"
    };

    private sealed record Runtime(
        PostgresHumanAuthenticationRepository Repository,
        HumanAuthenticationService Service,
        IHumanPasswordHasher Passwords,
        ITotpSecretProtector Protector,
        HumanAuthenticationOptions Options,
        TimeProvider Clock);

    private sealed record UserSeed(
        Guid UserId,
        string Username,
        string RoleCode,
        Guid UserRoleId,
        Guid SiteId,
        Guid SiteGroupId,
        byte[] TotpSecret);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
