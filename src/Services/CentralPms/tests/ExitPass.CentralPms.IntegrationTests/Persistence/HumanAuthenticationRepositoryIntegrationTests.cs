using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Operator_console_session_context_uses_canonical_device_shift_and_live_revocation()
    {
        var runtime = CreateRuntime(TestOptions());
        var user = await SeedUserAsync(runtime.Passwords, "I020OperatingContext", "correct horse battery staple");
        var (siteId, siteGroupId) = await GrantSeededRoleAndScopesAsync(user);
        var login = await runtime.Service.LoginAsync("i020operatingcontext", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        login.Response.Authenticated.Should().BeTrue();

        const string proof = "canonical-operator-device-proof-with-more-than-32-characters";
        var repository = new PostgresOperatorConsoleOperatingContextRepository(_database.ConnectionString);
        var service = new OperatorConsoleOperatingContextService(repository, TimeProvider.System,
            NullLogger<OperatorConsoleOperatingContextService>.Instance);
        var (deviceId, shiftId) = await SeedOperatorContextAsync(user, siteId, siteGroupId, service.HashDeviceProof(proof));
        var issued = await service.EstablishDeviceBindingAsync(proof, Guid.NewGuid(), default);
        issued.Succeeded.Should().BeTrue();
        issued.CookieCredential.Should().NotBe(proof);

        var bound = await service.BindSessionAsync(login.InternalHumanSessionId!.Value, user, [siteId], [siteGroupId], false, issued.CookieCredential, Guid.NewGuid(), default);
        bound.Succeeded.Should().BeTrue();
        bound.Context!.OperatorDeviceBindingId.Should().Be(deviceId);
        bound.Context.OperatorShiftId.Should().Be(shiftId);

        var live = await service.ValidateSessionAsync(login.InternalHumanSessionId.Value, issued.CookieCredential, Guid.NewGuid(), default);
        live.Succeeded.Should().BeTrue();

        await ExecuteAsync("""
            UPDATE operator_console.operator_device_bindings
            SET device_status='REVOKED', revoked_at=now(), revocation_reason_code='TEST', row_version=row_version+1
            WHERE operator_device_binding_id=@device_id;
            """, user, ("device_id", deviceId));
        var revoked = await service.ValidateSessionAsync(login.InternalHumanSessionId.Value, issued.CookieCredential, Guid.NewGuid(), default);
        revoked.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceBindingRevoked);
    }

    [Fact]
    public async Task Apt_operational_permissions_flow_from_canonical_role_bindings_without_role_name_authority()
    {
        var runtime = CreateRuntime(TestOptions());
        var user = await SeedUserAsync(runtime.Passwords, "I021AAptPermissions", "correct horse battery staple");
        var (siteId, siteGroupId) = await GrantSeededRoleAndScopesAsync(user);
        await GrantCanonicalPermissionsAsync(user, AptHumanPermissionCatalog.OperationalPermissions);
        var (deviceId, deviceSiteId) = await SeedAptDeviceAsync();
        deviceSiteId.Should().Be(siteId);
        var context = Context() with { DeviceServiceIdentityId = deviceId, SiteId = siteId };

        var login = await runtime.Service.LoginAsync("i021aaptpermissions", "correct horse battery staple",
            HumanSessionAudiences.Apt, null, context, CancellationToken.None);

        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.Permissions.Should().Contain(AptHumanPermissionCatalog.OperationalPermissions);
        login.Response.Session.SiteReferences.Should().Contain(siteId);
        login.Response.Session.SiteGroupReferences.Should().Contain(siteGroupId);
        login.Response.Session.HasGlobalScope.Should().BeFalse();

        await ExecuteAsync("""
            UPDATE identity.role_permissions rp
            SET binding_status='REVOKED', revoked_at=now(), revocation_reason_code='I021A_TEST', row_version=rp.row_version+1
            FROM identity.permissions p, identity.user_roles ur
            WHERE rp.permission_id=p.permission_id AND rp.role_id=ur.role_id
              AND ur.user_id=@user_id AND p.permission_code=@permission_code
              AND rp.binding_status='ACTIVE';
            """, user, ("permission_code", AptHumanPermissionCatalog.TerminalCashReceive));

        var refreshed = await runtime.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.Apt, deviceId, context, false, CancellationToken.None);
        refreshed.Response.Authenticated.Should().BeTrue();
        refreshed.Response.Session!.Permissions.Should().NotContain(AptHumanPermissionCatalog.TerminalCashReceive);
        refreshed.Response.Session.Permissions.Should().Contain(AptHumanPermissionCatalog.Access);
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
        await GrantSeededRoleAndScopesAsync(user);
        var credentialId = await ScalarAsync<Guid>("SELECT local_credential_id FROM identity.local_credentials WHERE user_id=@id;", user);
        var localVersionBefore = await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", user);
        var userVersionBefore = await ScalarAsync<long>("SELECT credential_version FROM identity.users WHERE user_id=@id;", user);
        var authorizationEpochBefore = await ScalarAsync<long>("SELECT authorization_epoch FROM identity.users WHERE user_id=@id;", user);
        var roleCountBefore = await ScalarAsync<int>("SELECT count(*)::integer FROM identity.user_roles WHERE user_id=@id;", user);
        var scopeCountBefore = await ScalarAsync<int>("SELECT count(*)::integer FROM identity.user_role_scope_grants WHERE user_role_id IN (SELECT user_role_id FROM identity.user_roles WHERE user_id=@id);", user);
        var operatorSession = await runtime.Service.LoginAsync("i020reset", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        var managementSession = await runtime.Service.LoginAsync("i020reset", "correct horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
        operatorSession.Response.Authenticated.Should().BeTrue();
        managementSession.Response.Authenticated.Should().BeTrue();
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.human_sessions WHERE user_id=@id AND session_status='ACTIVE';", user)).Should().Be(2);
        var now = DateTimeOffset.UtcNow;
        var correlation = Guid.NewGuid();
        var challenge = await runtime.Repository.CreateCredentialChallengeAsync(user, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, correlation, CancellationToken.None);
        var context = Context(correlation);

        var reset = await runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret,
            "replacement horse battery staple", context, CancellationToken.None);
        reset.HttpStatusCode.Should().Be(200);
        reset.Response.Outcome.Should().Be("PASSWORD_RESET_COMPLETED");
        reset.Response.Authenticated.Should().BeFalse();
        (await ScalarAsync<Guid>("SELECT local_credential_id FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(credentialId);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id AND credential_status IN ('ACTIVE','CHANGE_REQUIRED','LOCKED');", user)).Should().Be(1);
        (await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(localVersionBefore + 1);
        (await ScalarAsync<long>("SELECT credential_version FROM identity.users WHERE user_id=@id;", user)).Should().Be(userVersionBefore + 1);
        (await ScalarAsync<long>("SELECT authorization_epoch FROM identity.users WHERE user_id=@id;", user)).Should().Be(authorizationEpochBefore);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.user_roles WHERE user_id=@id;", user)).Should().Be(roleCountBefore);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.user_role_scope_grants WHERE user_role_id IN (SELECT user_role_id FROM identity.user_roles WHERE user_id=@id);", user)).Should().Be(scopeCountBefore);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.human_sessions WHERE user_id=@id AND session_status='ACTIVE';", user)).Should().Be(0);
        var revokedSession = await runtime.Service.ResolveSessionAsync(operatorSession.Credential!.SerializedToken,
            HumanSessionAudiences.OperatorConsole, null, Context(), false, CancellationToken.None);
        revokedSession.Response.Authenticated.Should().BeFalse();
        revokedSession.Response.ErrorCode.Should().BeOneOf("SESSION_REVOKED", "SESSION_EXPIRED");

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
        (await ScalarAsync<bool>("SELECT challenge_secret_hash <> @secret FROM identity.credential_challenges WHERE challenge_reference=@id;", challenge.Reference, ("secret", challenge.Secret))).Should().BeTrue();
    }

    [Fact]
    public async Task Password_reset_request_delivery_is_eligible_replay_safe_and_anti_enumerating_internally()
    {
        var delivery = new CapturingCredentialChallengeDelivery();
        var runtime = CreateRuntime(TestOptions(), challengeDelivery: delivery);
        var active = await SeedUserAsync(runtime.Passwords, $"W43Email{Guid.NewGuid():N}"[..28],
            "correct horse battery staple", "employee@example.test");
        var now = DateTimeOffset.UtcNow;
        var activation = await runtime.Repository.CreateCredentialChallengeAsync(active, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);

        await runtime.Service.RequestPasswordResetAsync((await ScalarAsync<string>("SELECT username FROM identity.users WHERE user_id=@id;", active)), Context(), CancellationToken.None);
        await runtime.Service.RequestPasswordResetAsync((await ScalarAsync<string>("SELECT username FROM identity.users WHERE user_id=@id;", active)), Context(), CancellationToken.None);

        delivery.Requests.Should().HaveCount(2);
        delivery.Requests.Should().OnlyContain(request => request.Purpose == "PASSWORD_RESET" && request.RecipientEmail == "employee@example.test");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.credential_challenges WHERE user_id=@id AND challenge_purpose='PASSWORD_RESET' AND challenge_status='ISSUED';", active)).Should().Be(1);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.credential_challenges WHERE user_id=@id AND challenge_purpose='PASSWORD_RESET' AND challenge_status='REVOKED';", active)).Should().Be(1);
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", activation.Reference)).Should().Be("ISSUED");

        var noEmail = await SeedUserAsync(runtime.Passwords, $"W43NoEmail{Guid.NewGuid():N}"[..28], "correct horse battery staple");
        await runtime.Service.RequestPasswordResetAsync((await ScalarAsync<string>("SELECT username FROM identity.users WHERE user_id=@id;", noEmail)), Context(), CancellationToken.None);
        await runtime.Service.RequestPasswordResetAsync($"W43Unknown{Guid.NewGuid():N}"[..28], Context(), CancellationToken.None);
        var invited = await SeedInvitedUserAsync($"W43Invited{Guid.NewGuid():N}"[..28]);
        await ExecuteAsync("UPDATE identity.users SET email='invited@example.test' WHERE user_id=@user_id;", invited);
        await runtime.Service.RequestPasswordResetAsync((await ScalarAsync<string>("SELECT username FROM identity.users WHERE user_id=@id;", invited)), Context(), CancellationToken.None);
        delivery.Requests.Should().HaveCount(2);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.credential_challenges WHERE user_id=@id AND challenge_purpose='PASSWORD_RESET';", noEmail)).Should().Be(0);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.credential_challenges WHERE user_id=@id AND challenge_purpose='PASSWORD_RESET';", invited)).Should().Be(0);

        var failed = await SeedUserAsync(runtime.Passwords, $"W43Failure{Guid.NewGuid():N}"[..28],
            "correct horse battery staple", "failure@example.test");
        delivery.ThrowOnDelivery = true;
        await runtime.Service.RequestPasswordResetAsync((await ScalarAsync<string>("SELECT username FROM identity.users WHERE user_id=@id;", failed)), Context(), CancellationToken.None);
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE user_id=@id AND challenge_purpose='PASSWORD_RESET';", failed)).Should().Be("REVOKED");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM audit.security_events WHERE target_entity_id IN (SELECT challenge_reference FROM identity.credential_challenges WHERE user_id=@id) AND reason_code='PASSWORD_RESET_EMAIL_DELIVERY_FAILED';", failed)).Should().Be(1);
    }

    [Fact]
    public async Task Password_reset_rejects_bad_expired_superseded_revoked_and_noneligible_challenges_without_rotation()
    {
        var runtime = CreateRuntime(TestOptions());
        var now = DateTimeOffset.UtcNow;

        var invalid = await SeedUserAsync(runtime.Passwords, $"W43Invalid{Guid.NewGuid():N}"[..28], "correct horse battery staple");
        var invalidChallenge = await runtime.Repository.CreateCredentialChallengeAsync(invalid, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        var invalidVersion = await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", invalid);
        var invalidSession = await runtime.Service.LoginAsync(await ScalarAsync<string>("SELECT username FROM identity.users WHERE user_id=@id;", invalid),
            "correct horse battery staple", HumanSessionAudiences.OperatorConsole, null, Context(), CancellationToken.None);
        (await runtime.Service.ResetPasswordAsync(invalidChallenge.Reference, "wrong-secret", "replacement horse battery staple", Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", invalid)).Should().Be(invalidVersion);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.human_sessions WHERE user_id=@id AND session_status='ACTIVE';", invalid)).Should().Be(1);
        (await runtime.Service.ResolveSessionAsync(invalidSession.Credential!.SerializedToken, HumanSessionAudiences.OperatorConsole, null, Context(), false, CancellationToken.None)).Response.Authenticated.Should().BeTrue();

        var expired = await SeedUserAsync(runtime.Passwords, $"W43Expired{Guid.NewGuid():N}"[..28], "correct horse battery staple");
        var expiredChallenge = await runtime.Repository.CreateCredentialChallengeAsync(expired, "PASSWORD_RESET",
            now.AddHours(-2), now.AddHours(-1), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ResetPasswordAsync(expiredChallenge.Reference, expiredChallenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", expiredChallenge.Reference)).Should().Be("EXPIRED");

        var superseded = await SeedUserAsync(runtime.Passwords, $"W43Superseded{Guid.NewGuid():N}"[..30], "correct horse battery staple");
        var oldChallenge = await runtime.Repository.CreateCredentialChallengeAsync(superseded, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        var latestChallenge = await runtime.Repository.CreateCredentialChallengeAsync(superseded, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ResetPasswordAsync(oldChallenge.Reference, oldChallenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await runtime.Service.ResetPasswordAsync(latestChallenge.Reference, latestChallenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None)).HttpStatusCode.Should().Be(200);

        var revoked = await SeedUserAsync(runtime.Passwords, $"W43Revoked{Guid.NewGuid():N}"[..28], "correct horse battery staple");
        var revokedChallenge = await runtime.Repository.CreateCredentialChallengeAsync(revoked, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        await runtime.Repository.RevokeCredentialChallengeAsync(revokedChallenge.Reference, CentralPmsServiceIdentityId,
            "TEST_REVOKED", now, CancellationToken.None);
        (await runtime.Service.ResetPasswordAsync(revokedChallenge.Reference, revokedChallenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");

        var invited = await SeedInvitedUserAsync($"W43INVITED{Guid.NewGuid():N}"[..28]);
        await SeedCurrentCredentialAsync(runtime.Passwords, invited, "PENDING_ACTIVATION");
        var invitedChallenge = await runtime.Repository.CreateCredentialChallengeAsync(invited, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ResetPasswordAsync(invitedChallenge.Reference, invitedChallenge.Secret,
            "replacement horse battery staple", Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", invited)).Should().Be("INVITED");

        foreach (var status in new[] { "INACTIVE", "SUSPENDED", "RETIRED" })
        {
            var nonEligible = await SeedUserAsync(runtime.Passwords, $"W43{status}{Guid.NewGuid():N}"[..28], "correct horse battery staple");
            await ExecuteAsync("UPDATE identity.users SET user_status=@status::identity.user_status_enum, row_version=row_version+1 WHERE user_id=@user_id;", nonEligible, ("status", status));
            var challenge = await runtime.Repository.CreateCredentialChallengeAsync(nonEligible, "PASSWORD_RESET", now,
                now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
            (await runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
            (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", nonEligible)).Should().Be(status);
        }
    }

    [Fact]
    public async Task Password_reset_policy_retry_and_concurrency_rotate_once_and_preserve_privileged_mfa()
    {
        var runtime = CreateRuntime(TestOptions());
        var username = $"W43Privileged{Guid.NewGuid():N}"[..30];
        var user = await SeedUserAsync(runtime.Passwords, username, "correct horse battery staple");
        await AssignPrivilegedRoleAsync(user);
        var authenticatorId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(20);
        var envelope = runtime.Protector.Protect(user, authenticatorId, secret);
        await SeedActiveAuthenticatorAsync(user, authenticatorId, envelope, runtime.Protector);
        CryptographicOperations.ZeroMemory(secret);
        var now = DateTimeOffset.UtcNow;
        var challenge = await runtime.Repository.CreateCredentialChallengeAsync(user, "PASSWORD_RESET", now,
            now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        var versionBefore = await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", user);

        var policyRejected = await runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret, "short", Context(), CancellationToken.None);
        policyRejected.Response.ErrorCode.Should().Be("PASSWORD_POLICY_FAILED");
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", challenge.Reference)).Should().Be("ISSUED");
        (await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(versionBefore);

        var attempts = await Task.WhenAll(
            runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None),
            runtime.Service.ResetPasswordAsync(challenge.Reference, challenge.Secret, "replacement horse battery staple", Context(), CancellationToken.None));
        attempts.Count(result => result.HttpStatusCode == 200).Should().Be(1);
        attempts.Count(result => result.Response.ErrorCode == "INVALID_OR_EXPIRED_CHALLENGE").Should().Be(1);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(1);
        (await ScalarAsync<long>("SELECT credential_version FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(versionBefore + 1);
        (await ScalarAsync<string>("SELECT authenticator_status::text FROM identity.user_mfa_authenticators WHERE user_id=@id;", user)).Should().Be("ACTIVE");

        var login = await runtime.Service.LoginAsync(username, "replacement horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
        login.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
        login.Response.Authenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Invited_user_activation_creates_first_credential_atomically_and_supports_ordinary_login()
    {
        var runtime = CreateRuntime(TestOptions());
        var username = $"w42ordinary{Guid.NewGuid():N}"[..28];
        var user = await SeedInvitedUserAsync(username);
        await AssignOrdinaryRoleAsync(user);
        var now = DateTimeOffset.UtcNow;
        var correlation = Guid.NewGuid();
        var challenge = await runtime.Repository.CreateCredentialChallengeAsync(user, "ACCOUNT_ACTIVATION",
            "ACCOUNT_ACTIVATION_ADMIN_ISSUED", now, now.AddMinutes(10), CentralPmsServiceIdentityId,
            correlation, CancellationToken.None);

        var activated = await runtime.Service.ActivateAsync(challenge.Reference, challenge.Secret,
            "correct horse battery staple", Context(correlation), CancellationToken.None);

        activated.HttpStatusCode.Should().Be(200);
        activated.Response.Outcome.Should().Be("ACCOUNT_ACTIVATED");
        activated.Response.Authenticated.Should().BeFalse();
        (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", user)).Should().Be("ACTIVE");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(1);
        (await ScalarAsync<string>("SELECT credential_status::text FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be("ACTIVE");
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", challenge.Reference)).Should().Be("CONSUMED");

        var login = await runtime.Service.LoginAsync(username, "correct horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.MfaRequired.Should().BeFalse();

        (await ScalarAsync<int>("""
            SELECT count(*)::integer FROM identity.authentication_attempts
            WHERE user_id=@id AND attempt_type='ACTIVATION_CHALLENGE' AND attempt_result='SUCCESS'
              AND reason_code='ACCOUNT_ACTIVATED';
            """, user)).Should().Be(1);
        (await ScalarAsync<int>("""
            SELECT count(*)::integer FROM audit.security_events se
            JOIN audit.audit_events ae ON ae.audit_event_id=se.audit_event_id
            WHERE se.correlation_id=@id AND se.security_event_type='ACTIVATION_CHALLENGE_CONSUMED'
              AND ae.event_type='ACTIVATION_CHALLENGE_CONSUMED';
            """, correlation)).Should().Be(1);
    }

    [Fact]
    public async Task Activation_rejects_invalid_expired_superseded_cancelled_active_and_conflicting_cases_without_credentials()
    {
        var runtime = CreateRuntime(TestOptions());
        var now = DateTimeOffset.UtcNow;

        var invalidUser = await SeedInvitedUserAsync($"w42invalid{Guid.NewGuid():N}"[..27]);
        var invalid = await runtime.Repository.CreateCredentialChallengeAsync(invalidUser, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ActivateAsync(invalid.Reference, "not-the-secret", "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        await AssertInvitedWithoutCredentialAsync(invalidUser);

        var expiredUser = await SeedInvitedUserAsync($"w42expired{Guid.NewGuid():N}"[..27]);
        var expired = await runtime.Repository.CreateCredentialChallengeAsync(expiredUser, "ACCOUNT_ACTIVATION",
            now.AddHours(-2), now.AddHours(-1), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ActivateAsync(expired.Reference, expired.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        await AssertInvitedWithoutCredentialAsync(expiredUser);
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", expired.Reference)).Should().Be("EXPIRED");

        var supersededUser = await SeedInvitedUserAsync($"w42superseded{Guid.NewGuid():N}"[..30]);
        var superseded = await runtime.Repository.CreateCredentialChallengeAsync(supersededUser, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        var latest = await runtime.Repository.CreateCredentialChallengeAsync(supersededUser, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ActivateAsync(superseded.Reference, superseded.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await runtime.Service.ActivateAsync(latest.Reference, latest.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).HttpStatusCode.Should().Be(200);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", supersededUser)).Should().Be(1);

        var cancelledUser = await SeedInvitedUserAsync($"w42cancelled{Guid.NewGuid():N}"[..29]);
        var cancelled = await runtime.Repository.CreateCredentialChallengeAsync(cancelledUser, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        await runtime.Repository.RevokeCredentialChallengeAsync(cancelled.Reference, CentralPmsServiceIdentityId,
            "INVITATION_CANCELLED", now, CancellationToken.None);
        await ExecuteAsync("UPDATE identity.users SET user_status='INACTIVE', row_version=row_version+1 WHERE user_id=@user_id;", cancelledUser);
        (await runtime.Service.ActivateAsync(cancelled.Reference, cancelled.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", cancelledUser)).Should().Be(0);

        var activeUser = await SeedInvitedUserAsync($"w42active{Guid.NewGuid():N}"[..26]);
        var activeChallenge = await runtime.Repository.CreateCredentialChallengeAsync(activeUser, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        await ExecuteAsync("UPDATE identity.users SET user_status='ACTIVE', row_version=row_version+1 WHERE user_id=@user_id;", activeUser);
        (await runtime.Service.ActivateAsync(activeChallenge.Reference, activeChallenge.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", activeUser)).Should().Be(0);

        var conflictUser = await SeedInvitedUserAsync($"w42conflict{Guid.NewGuid():N}"[..28]);
        await SeedCurrentCredentialAsync(runtime.Passwords, conflictUser, "ACTIVE");
        var conflict = await runtime.Repository.CreateCredentialChallengeAsync(conflictUser, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        (await runtime.Service.ActivateAsync(conflict.Reference, conflict.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");
        (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", conflictUser)).Should().Be("INVITED");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", conflictUser)).Should().Be(1);
    }

    [Fact]
    public async Task Activation_password_policy_is_retryable_and_pending_bootstrap_credential_is_preserved()
    {
        var runtime = CreateRuntime(TestOptions());
        var user = await SeedInvitedUserAsync($"w42retry{Guid.NewGuid():N}"[..25]);
        var credentialId = await SeedCurrentCredentialAsync(runtime.Passwords, user, "PENDING_ACTIVATION");
        var now = DateTimeOffset.UtcNow;
        var challenge = await runtime.Repository.CreateCredentialChallengeAsync(user, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);

        var rejected = await runtime.Service.ActivateAsync(challenge.Reference, challenge.Secret, "short",
            Context(), CancellationToken.None);
        rejected.Response.ErrorCode.Should().Be("PASSWORD_POLICY_FAILED");
        (await ScalarAsync<string>("SELECT challenge_status::text FROM identity.credential_challenges WHERE challenge_reference=@id;", challenge.Reference)).Should().Be("ISSUED");
        (await ScalarAsync<string>("SELECT credential_status::text FROM identity.local_credentials WHERE local_credential_id=@id;", credentialId)).Should().Be("PENDING_ACTIVATION");

        var activated = await runtime.Service.ActivateAsync(challenge.Reference, challenge.Secret,
            "correct horse battery staple", Context(), CancellationToken.None);
        activated.HttpStatusCode.Should().Be(200);
        (await ScalarAsync<Guid>("SELECT local_credential_id FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(credentialId);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_activation_and_replay_allow_exactly_one_success_and_privileged_login_remains_mfa_restricted()
    {
        var runtime = CreateRuntime(TestOptions());
        var username = $"w42privileged{Guid.NewGuid():N}"[..30];
        var user = await SeedInvitedUserAsync(username);
        await AssignPrivilegedRoleAsync(user);
        var now = DateTimeOffset.UtcNow;
        var challenge = await runtime.Repository.CreateCredentialChallengeAsync(user, "ACCOUNT_ACTIVATION",
            now, now.AddMinutes(10), CentralPmsServiceIdentityId, Guid.NewGuid(), CancellationToken.None);
        var context = Context();

        var attempts = await Task.WhenAll(
            runtime.Service.ActivateAsync(challenge.Reference, challenge.Secret, "correct horse battery staple", context, CancellationToken.None),
            runtime.Service.ActivateAsync(challenge.Reference, challenge.Secret, "correct horse battery staple", context, CancellationToken.None));

        attempts.Count(result => result.HttpStatusCode == 200).Should().Be(1);
        attempts.Count(result => result.Response.ErrorCode == "INVALID_OR_EXPIRED_CHALLENGE").Should().Be(1);
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", user)).Should().Be(1);
        (await runtime.Service.ActivateAsync(challenge.Reference, challenge.Secret, "correct horse battery staple",
            Context(), CancellationToken.None)).Response.ErrorCode.Should().Be("INVALID_OR_EXPIRED_CHALLENGE");

        var login = await runtime.Service.LoginAsync(username, "correct horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, Context(), CancellationToken.None);
        login.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.MfaEnrollmentRequired);
        login.Response.Authenticated.Should().BeTrue();
        login.Response.Session!.MfaRequired.Should().BeTrue();
        login.Response.Session.MfaSatisfied.Should().BeFalse();
        login.Response.Session.Permissions.Should().BeEmpty();
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
        liveAuthorization.Response.Authenticated.Should().BeFalse();
        liveAuthorization.Response.ErrorCode.Should().Be("SESSION_REVOKED");

        var credentialLogin = await runtime.Service.LoginAsync("i020epoch", "correct horse battery staple",
            HumanSessionAudiences.OperatorConsole, null, context, CancellationToken.None);
        await ExecuteAsync("UPDATE identity.users SET credential_version=credential_version+1 WHERE user_id=@user_id;", user);
        var invalidated = await runtime.Service.ResolveSessionAsync(credentialLogin.Credential!.SerializedToken,
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
    public async Task Web_session_uses_thirty_minute_sliding_idle_and_fixed_eight_hour_absolute_expiry()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var options = TestOptions();
        options.WebIdleMinutes.Should().Be(30);
        options.WebAbsoluteHours.Should().Be(8);
        var runtime = CreateRuntime(options, clock);
        var user = await SeedUserAsync(runtime.Passwords, "I020Sliding", "correct horse battery staple");
        await GrantSeededRoleAndScopesAsync(user);
        var context = Context();

        var login = await runtime.Service.LoginAsync("i020sliding", "correct horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, context, CancellationToken.None);
        var initial = login.Response.Session!;
        initial.IdleExpiresAt.Should().Be(initial.AuthenticatedAt.AddMinutes(30));
        initial.AbsoluteExpiresAt.Should().Be(initial.AuthenticatedAt.AddHours(8));

        clock.Advance(TimeSpan.FromMinutes(16));
        var active = await runtime.Service.ResolveSessionAsync(login.Credential!.SerializedToken,
            HumanSessionAudiences.ManagementPlatform, null, context, true, CancellationToken.None);
        active.Response.Authenticated.Should().BeTrue();
        active.Response.Session!.MfaRequired.Should().Be(initial.MfaRequired);
        active.Response.Session.MfaSatisfied.Should().Be(initial.MfaSatisfied);
        active.Response.Session.IdleExpiresAt.Should().Be(clock.GetUtcNow().AddMinutes(30));
        active.Response.Session.AbsoluteExpiresAt.Should().Be(initial.AbsoluteExpiresAt);

        var inactiveLogin = await runtime.Service.LoginAsync("i020sliding", "correct horse battery staple",
            HumanSessionAudiences.ManagementPlatform, null, context, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(30).Add(TimeSpan.FromSeconds(1)));
        var expired = await runtime.Service.ResolveSessionAsync(inactiveLogin.Credential!.SerializedToken,
            HumanSessionAudiences.ManagementPlatform, null, context, false, CancellationToken.None);
        expired.HttpStatusCode.Should().Be(401);
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

    private Runtime CreateRuntime(
        HumanAuthenticationOptions options,
        TimeProvider? timeProvider = null,
        ICredentialChallengeDelivery? challengeDelivery = null)
    {
        var configured = Options.Create(options);
        var tokens = new HumanSessionTokenService();
        var repository = new PostgresHumanAuthenticationRepository(_database.ConnectionString, tokens);
        var passwords = new Argon2idHumanPasswordHasher(configured);
        var protector = new AesGcmTotpSecretProtector(configured);
        var service = new HumanAuthenticationService(repository, passwords, new TotpProvider(configured),
            protector, tokens, challengeDelivery ?? new DisabledCredentialChallengeDelivery(),
            timeProvider ?? TimeProvider.System, configured);
        return new Runtime(repository, service, passwords, protector);
    }

    private async Task<Guid> SeedInvitedUserAsync(string username)
    {
        var userId = Guid.NewGuid();
        const string sql = """
            INSERT INTO identity.users (user_id,username,display_name,user_type,user_status,effective_from,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_id,@username,@username,'SITE_OPERATOR','INVITED',now()-interval '1 day',@service_id,@service_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        await command.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task<Guid> SeedCurrentCredentialAsync(
        IHumanPasswordHasher passwords,
        Guid userId,
        string status)
    {
        var credentialId = Guid.NewGuid();
        var material = await passwords.HashAsync("legacy bootstrap horse battery staple", CancellationToken.None);
        const string sql = """
            INSERT INTO identity.local_credentials (local_credential_id,user_id,credential_status,password_verifier,
                verifier_salt,verifier_algorithm_code,verifier_algorithm_version,verifier_work_factor,
                verifier_memory_kib,verifier_parallelism,activated_at,last_changed_at,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@credential_id,@user_id,@status::identity.local_credential_status_enum,@verifier,@salt,
                @algorithm,@algorithm_version,@work_factor,@memory_kib,@parallelism,
                CASE WHEN @status='PENDING_ACTIVATION' THEN NULL ELSE now() END,
                CASE WHEN @status='PENDING_ACTIVATION' THEN NULL ELSE now() END,@service_id,@service_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("credential_id", credentialId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("verifier", material.Verifier);
        command.Parameters.AddWithValue("salt", material.Salt);
        command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
        command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
        command.Parameters.AddWithValue("work_factor", material.Iterations);
        command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
        command.Parameters.AddWithValue("parallelism", material.Parallelism);
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        await command.ExecuteNonQueryAsync();
        return credentialId;
    }

    private async Task AssignOrdinaryRoleAsync(Guid userId)
    {
        const string sql = """
            INSERT INTO identity.user_roles (user_role_id,user_id,role_id,assignment_status,assignment_reason_code,
                assigned_by_service_identity_id,effective_from,created_by_service_identity_id,updated_by_service_identity_id)
            SELECT gen_random_uuid(),@user_id,role_id,'ACTIVE','W42_TEST',@service_id,now()-interval '1 day',@service_id,@service_id
            FROM identity.roles
            WHERE role_status='ACTIVE' AND NOT is_privileged
            ORDER BY role_code LIMIT 1;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task AssertInvitedWithoutCredentialAsync(Guid userId)
    {
        (await ScalarAsync<string>("SELECT user_status::text FROM identity.users WHERE user_id=@id;", userId)).Should().Be("INVITED");
        (await ScalarAsync<int>("SELECT count(*)::integer FROM identity.local_credentials WHERE user_id=@id;", userId)).Should().Be(0);
    }

    private async Task<Guid> SeedUserAsync(
        IHumanPasswordHasher passwords,
        string username,
        string password,
        string? email = null)
    {
        var userId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var material = await passwords.HashAsync(password, CancellationToken.None);
        const string sql = """
            INSERT INTO identity.users (user_id,username,display_name,email,user_type,user_status,effective_from,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@user_id,@username,@username,@email,'SITE_OPERATOR','ACTIVE',now()-interval '1 day',@service_id,@service_id);
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
        command.Parameters.Add("email", NpgsqlTypes.NpgsqlDbType.Varchar).Value = (object?)email ?? DBNull.Value;
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
        var (siteId, siteGroupId) = await ActivateCanonicalPitxLevel3Async();
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

    private async Task GrantCanonicalPermissionsAsync(Guid userId, IReadOnlyList<string> permissionCodes)
    {
        const string sql = """
            INSERT INTO identity.permissions (
                permission_id, permission_code, permission_name, permission_description,
                permission_domain, permission_action, permission_status, is_sensitive, requires_audit,
                created_by_service_identity_id, updated_by_service_identity_id)
            SELECT gen_random_uuid(), code, code, 'I-021A disposable canonical permission binding proof.',
                   'APT', 'OPERATE', 'ACTIVE', true, true, @service_id, @service_id
            FROM unnest(@permission_codes::varchar[]) AS code
            ON CONFLICT (permission_code) DO NOTHING;

            INSERT INTO identity.role_permissions (
                role_permission_id, role_id, permission_id, binding_status, binding_reason_code,
                assigned_by_service_identity_id, effective_from,
                created_by_service_identity_id, updated_by_service_identity_id)
            SELECT gen_random_uuid(), ur.role_id, p.permission_id, 'ACTIVE', 'I021A_TEST',
                   @service_id, now()-interval '1 day', @service_id, @service_id
            FROM identity.user_roles ur
            JOIN identity.permissions p ON p.permission_code=ANY(@permission_codes::varchar[])
            WHERE ur.user_id=@user_id AND ur.assignment_status='ACTIVE'
            ON CONFLICT DO NOTHING;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("permission_codes", permissionCodes.ToArray());
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        await command.ExecuteNonQueryAsync();
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
        var (siteId, _) = await ActivateCanonicalPitxLevel3Async();
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

    private async Task<(Guid SiteId, Guid SiteGroupId)> ActivateCanonicalPitxLevel3Async()
    {
        var siteGroupId = Guid.Parse("a6dbadf6-68b5-5bed-a7e0-a75faee70841");
        var siteId = Guid.Parse("2d1dcdf8-f563-537c-8542-0bde7cc9da97");
        const string sql = """
            UPDATE sites.site_groups SET site_group_status='ACTIVE' WHERE site_group_id=@site_group_id;
            UPDATE sites.sites SET site_status='ACTIVE' WHERE site_id=@site_id AND site_group_id=@site_group_id;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("site_id", siteId);
        (await command.ExecuteNonQueryAsync()).Should().Be(2);
        return (siteId, siteGroupId);
    }

    private async Task<(Guid DeviceId, Guid ShiftId)> SeedOperatorContextAsync(Guid userId, Guid siteId, Guid siteGroupId, string proofThumbprint)
    {
        var deviceId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        const string sql = """
            INSERT INTO operator_console.hr_identity_mappings (
                hr_identity_mapping_id,user_id,hr_provider_code,external_person_id_hash,mapping_status,
                effective_from,correlation_id,created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@mapping_id,@user_id,'TEST',@person_hash,'ACTIVE',now()-interval '1 day',gen_random_uuid(),@service_id,@service_id);

            INSERT INTO operator_console.operator_device_bindings (
                operator_device_binding_id,device_binding_code,device_name,site_group_id,site_id,
                browser_key_thumbprint,device_status,trust_level,binding_source,correlation_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@device_id,@device_code,'Integration browser',@site_group_id,@site_id,
                @proof_thumbprint,'ACTIVE','BROWSER_KEY_ONLY','TEST',gen_random_uuid(),@service_id,@service_id);

            INSERT INTO operator_console.operator_device_assignment_history (
                operator_device_assignment_history_id,operator_device_binding_id,site_group_id,site_id,
                assignment_status_code,assignment_source_code,assigned_at,effective_from,correlation_id,
                assigned_by_service_identity_id,created_by_service_identity_id)
            VALUES (gen_random_uuid(),@device_id,@site_group_id,@site_id,'ACTIVE','TEST',now(),now()-interval '1 day',
                gen_random_uuid(),@service_id,@service_id);

            INSERT INTO operator_console.operator_shifts (
                operator_shift_id,shift_reference,shift_origin,hr_provider_code,external_shift_id_hash,hr_identity_mapping_id,operator_user_id,
                site_group_id,site_id,scheduled_start_at,scheduled_end_at,source_imported_at,import_status_code,
                source_system_code,operational_status,active_from,active_to,opened_at,correlation_id,
                created_by_service_identity_id,updated_by_service_identity_id)
            VALUES (@shift_id,@shift_reference,'HR_IMPORT','TEST',@shift_hash,@mapping_id,@user_id,@site_group_id,@site_id,
                now()-interval '1 hour',now()+interval '8 hours',now(),'IMPORTED','TEST','ACTIVE',
                now()-interval '1 hour',now()+interval '8 hours',now()-interval '1 hour',gen_random_uuid(),@service_id,@service_id);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mapping_id", mappingId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("person_hash", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("device_code", $"I020_CTX_{deviceId:N}"[..32]);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("proof_thumbprint", proofThumbprint);
        command.Parameters.AddWithValue("shift_id", shiftId);
        command.Parameters.AddWithValue("shift_reference", $"I020-{shiftId:N}");
        command.Parameters.AddWithValue("shift_hash", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
        command.Parameters.AddWithValue("service_id", CentralPmsServiceIdentityId);
        await command.ExecuteNonQueryAsync();
        return (deviceId, shiftId);
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

    private async Task<T> ScalarAsync<T>(string sql, Guid id, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
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

    private sealed class CapturingCredentialChallengeDelivery : ICredentialChallengeDelivery
    {
        public bool Enabled => true;
        public bool ThrowOnDelivery { get; set; }
        public List<CredentialChallengeDeliveryRequest> Requests { get; } = [];

        public Task DeliverAsync(CredentialChallengeDeliveryRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ThrowOnDelivery
                ? Task.FromException(new InvalidOperationException("Task-owned delivery failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
