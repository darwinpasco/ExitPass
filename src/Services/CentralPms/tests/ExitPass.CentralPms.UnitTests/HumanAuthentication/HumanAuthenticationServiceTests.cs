using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.HumanAuthentication;

public sealed class HumanAuthenticationServiceTests
{
    [Fact]
    public void Principal_EmitsDeviceAndShiftClaimsOnlyFromServerOwnedSessionFields()
    {
        var deviceId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var session = new HumanSessionDto(
            Guid.NewGuid(), Guid.NewGuid(), "operator", "Operator", HumanSessionAudiences.OperatorConsole,
            "PASSWORD", false, false, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30), DateTimeOffset.UtcNow.AddHours(8), [], [siteId], [groupId],
            false, null, Guid.NewGuid(), deviceId, shiftId, siteId, groupId, 3, 5);

        var principal = HumanSessionAuthenticationHandler.CreatePrincipal(session, Guid.NewGuid());

        principal.FindFirst("operator_device_binding_id")!.Value.Should().Be(deviceId.ToString("D"));
        principal.FindFirst("operator_shift_id")!.Value.Should().Be(shiftId.ToString("D"));
        principal.FindFirst("operator_effective_site_id")!.Value.Should().Be(siteId.ToString("D"));
        principal.FindFirst("operator_effective_site_group_id")!.Value.Should().Be(groupId.ToString("D"));
        principal.FindFirst("authorization_epoch")!.Value.Should().Be("3");
        principal.FindFirst("credential_version")!.Value.Should().Be("5");
    }

    [Fact]
    public void SessionResponse_DoesNotSerializeCanonicalOperatingContextStorageReferences()
    {
        var session = new HumanSessionDto(
            Guid.NewGuid(), Guid.NewGuid(), "operator", "Operator", HumanSessionAudiences.OperatorConsole,
            "PASSWORD", false, false, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30), DateTimeOffset.UtcNow.AddHours(8), [], [], [],
            false, null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 5);

        var json = JsonSerializer.Serialize(session);

        json.Should().NotContain("OperatorDeviceBindingReference")
            .And.NotContain("OperatorShiftReference")
            .And.NotContain("EffectiveSiteReference")
            .And.NotContain("EffectiveSiteGroupReference")
            .And.NotContain("AuthorizationEpoch")
            .And.NotContain("CredentialVersion");
    }

    [Theory]
    [InlineData(HumanSessionAudiences.ManagementPlatform)]
    [InlineData(HumanSessionAudiences.OperatorConsole)]
    public async Task Ordinary_web_user_authenticates_without_mfa(string audience)
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false);
        var result = await fixture.LoginAsync(audience);
        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.MfaRequired.Should().BeFalse();
        result.Response.AptSessionToken.Should().BeNull();
    }

    [Fact]
    public async Task Operator_console_user_authenticates_without_site_shift_schedule_or_custody()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false);
        fixture.Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new EffectiveHumanAuthorization([], [], [], false));

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.SiteReferences.Should().BeEmpty();
        result.Response.Session.SiteGroupReferences.Should().BeEmpty();
    }

    [Fact]
    public async Task Privileged_management_user_without_authenticator_gets_restricted_enrollment_session()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true);
        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform);
        result.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.MfaEnrollmentRequired);
        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.Permissions.Should().BeEmpty();
        result.Response.Session.MfaRequired.Should().BeTrue();
        result.Response.Session.MfaSatisfied.Should().BeFalse();
    }

    [Fact]
    public async Task Privileged_management_user_with_active_authenticator_requires_totp()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator);
        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform);
        result.HttpStatusCode.Should().Be(401);
        result.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.MfaRequired);
    }

    [Fact]
    public async Task Privileged_management_user_accepts_one_totp_step_once()
    {
        var fixture = new Fixture(totpSucceeds: true);
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator);
        fixture.Repository.TryRecordTotpSuccessAsync(fixture.Authenticator.AuthenticatorId, 1, 123, Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform, "123456");
        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.MfaSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task Replayed_totp_is_rejected_when_atomic_step_update_loses()
    {
        var fixture = new Fixture(totpSucceeds: true);
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator);
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform, "123456");
        result.Response.Authenticated.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("TOTP_INVALID");
    }

    [Fact]
    public async Task Operator_console_does_not_require_totp_even_for_privileged_role()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator);
        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);
        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.MfaRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Apt_requires_active_device_site_binding_and_returns_device_bound_token()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator);
        fixture.Repository.IsActiveDeviceServiceAtSiteAsync(fixture.DeviceId, fixture.SiteId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var result = await fixture.LoginAsync(HumanSessionAudiences.Apt, deviceId: fixture.DeviceId, siteId: fixture.SiteId);
        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.MfaRequired.Should().BeFalse();
        result.Response.AptSessionToken.Should().NotBeNullOrWhiteSpace();
        result.Response.Session.DeviceServiceIdentityReference.Should().Be(fixture.DeviceId);
    }

    [Fact]
    public async Task Apt_fails_closed_without_device_trust()
    {
        var fixture = new Fixture();
        var result = await fixture.LoginAsync(HumanSessionAudiences.Apt);
        result.HttpStatusCode.Should().Be(403);
        result.Response.ErrorCode.Should().Be("APT_DEVICE_TRUST_REQUIRED");
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_share_the_same_public_error()
    {
        var unknown = new Fixture { Login = null };
        var wrong = new Fixture(passwordSucceeds: false) { Login = new Fixture().CreateLogin(false) };
        var unknownResult = await unknown.LoginAsync(HumanSessionAudiences.OperatorConsole);
        var wrongResult = await wrong.LoginAsync(HumanSessionAudiences.OperatorConsole);
        unknownResult.Response.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        wrongResult.Response.ErrorCode.Should().Be(unknownResult.Response.ErrorCode);
        wrongResult.HttpStatusCode.Should().Be(unknownResult.HttpStatusCode);
    }

    [Theory]
    [InlineData("LOCKED")]
    [InlineData("SUSPENDED")]
    [InlineData("INACTIVE")]
    [InlineData("RETIRED")]
    public async Task Non_active_accounts_fail_with_anti_enumerating_error(string status)
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(false) with { UserStatus = status };
        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);
        result.Response.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Repeated_invalid_current_password_locks_account_during_password_change()
    {
        var fixture = new Fixture(passwordSucceeds: false);
        fixture.Login = fixture.CreateLogin(false);
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        var session = Fixture.Session(fixture.Login.UserId, token.SessionReference,
            HumanSessionAudiences.OperatorConsole, null, false, null, null, "PASSWORD",
            now, now.AddMinutes(15), now.AddHours(8), Guid.NewGuid(), false) with
        {
            SessionSecretHash = fixture.Tokens.HashSecret(token.Secret)
        };
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(session);
        fixture.Repository.CountRecentFailedAttemptsAsync(fixture.Login.UserId, Arg.Any<string>(), Arg.Any<string?>(),
            "PASSWORD", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(4);

        var result = await fixture.Service.ChangePasswordAsync(token.SerializedToken, "wrong-password",
            "a-valid-new-password", null, fixture.Context(), CancellationToken.None);

        result.Response.ErrorCode.Should().Be("CURRENT_PASSWORD_INVALID");
        await fixture.Repository.Received(1).ApplyAuthenticationLockoutAsync(fixture.Login.UserId,
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), "AUTHENTICATION_FAILURE", Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveSession_WhenAuthorizationEpochChanges_RevokesTheStaleSession()
    {
        var fixture = new Fixture();
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        var session = Fixture.Session(Guid.NewGuid(), token.SessionReference,
            HumanSessionAudiences.OperatorConsole, null, false, null, null, "PASSWORD",
            now, now.AddMinutes(15), now.AddHours(8), Guid.NewGuid(), false) with
        {
            SessionSecretHash = fixture.Tokens.HashSecret(token.Secret),
            AuthorizationEpochSnapshot = 4,
            CurrentAuthorizationEpoch = 5
        };
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(session);

        var result = await fixture.Service.ResolveSessionAsync(
            token.SerializedToken,
            HumanSessionAudiences.OperatorConsole,
            null,
            fixture.Context(),
            false,
            CancellationToken.None);

        result.Response.Authenticated.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("SESSION_REVOKED");
        await fixture.Repository.Received(1).RevokeSessionAsync(
            session.HumanSessionId,
            session.UserId,
            "IDENTITY_OR_CREDENTIAL_CHANGED",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public readonly IHumanAuthenticationRepository Repository = Substitute.For<IHumanAuthenticationRepository>();
        public readonly IHumanPasswordHasher Passwords = Substitute.For<IHumanPasswordHasher>();
        public readonly ITotpProvider Totp = Substitute.For<ITotpProvider>();
        public readonly ITotpSecretProtector Protector = Substitute.For<ITotpSecretProtector>();
        public readonly ICredentialChallengeDelivery ChallengeDelivery = Substitute.For<ICredentialChallengeDelivery>();
        public readonly IHumanSessionTokenService Tokens = new HumanSessionTokenService();
        public readonly Guid DeviceId = Guid.NewGuid();
        public readonly Guid SiteId = Guid.NewGuid();
        public readonly TotpAuthenticatorRecord Authenticator = new(Guid.NewGuid(), "ACTIVE", new byte[48], "test", "1", 1, null, 1);
        public HumanLoginRecord? Login { get; set; }
        public HumanAuthenticationService Service { get; }

        public Fixture(bool passwordSucceeds = true, bool totpSucceeds = false)
        {
            var options = Options.Create(new HumanAuthenticationOptions());
            Passwords.VerifyAsync(Arg.Any<string>(), Arg.Any<LocalCredentialRecord?>(), Arg.Any<CancellationToken>()).Returns(passwordSucceeds);
            Passwords.NeedsUpgrade(Arg.Any<LocalCredentialRecord>()).Returns(false);
            Protector.IsConfigured.Returns(true);
            ChallengeDelivery.Enabled.Returns(false);
            Protector.Unprotect(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<TotpAuthenticatorRecord>()).Returns(new byte[20]);
            Totp.Verify(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>()).Returns(new TotpVerificationResult(totpSucceeds, totpSucceeds ? 123 : null));
            Repository.FindLocalLoginAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => Login);
            Repository.CountRecentFailedAttemptsAsync(Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
            Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new EffectiveHumanAuthorization(["test.permission"], [SiteId], [], false));
            Repository.CreateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<SessionCredential>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var token = call.ArgAt<SessionCredential>(14);
                    var record = Session(call.ArgAt<Guid>(0), token.SessionReference, call.ArgAt<string>(2), call.ArgAt<Guid?>(3), call.ArgAt<bool>(4), call.ArgAt<Guid?>(5), call.ArgAt<DateTimeOffset?>(6), call.ArgAt<string>(7), call.ArgAt<DateTimeOffset>(10), call.ArgAt<DateTimeOffset>(11), call.ArgAt<DateTimeOffset>(12), call.ArgAt<Guid>(13), Login?.HasPrivilegedRole ?? false);
                    return new SessionIssue(record.HumanSessionId, token, record);
                });
            Service = new HumanAuthenticationService(Repository, Passwords, Totp, Protector, Tokens, ChallengeDelivery, TimeProvider.System, options);
        }

        public Task<HumanAuthenticationResult> LoginAsync(string audience, string? totp = null, Guid? deviceId = null, Guid? siteId = null) =>
            Service.LoginAsync("Cashier01", "valid-password", audience, totp, Context(deviceId, siteId), CancellationToken.None);

        public HumanAuthenticationContext Context(Guid? deviceId = null, Guid? siteId = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), new string('b', 64), deviceId, siteId);

        public HumanLoginRecord CreateLogin(bool privileged, TotpAuthenticatorRecord? authenticator = null)
        {
            var credential = new LocalCredentialRecord(Guid.NewGuid(), "ACTIVE", new byte[32], new byte[16], "ARGON2ID", 19, 3, 65536, 1, 1, 1);
            return new HumanLoginRecord(Guid.NewGuid(), "Cashier01", "Cashier One", "ACTIVE", DateTimeOffset.UtcNow.AddDays(-1), null, null, null, 1, 1, privileged, credential, authenticator);
        }

        public static HumanSessionRecord Session(Guid userId, Guid reference, string audience, Guid? deviceId, bool mfa, Guid? mfaId, DateTimeOffset? mfaAt, string assurance, DateTimeOffset now, DateTimeOffset idle, DateTimeOffset absolute, Guid correlation, bool privileged) =>
            new(Guid.NewGuid(), reference, new string('a', 64), userId, "Cashier01", "Cashier One", "ACTIVE", now.AddDays(-1), null, null, "LOCAL", Guid.NewGuid(), "ACTIVE", null, audience, deviceId, "ACTIVE", assurance, mfa, mfaId, mfaAt, now, now, idle, absolute, 1, 1, 1, 1, privileged, correlation, 1);
    }
}
