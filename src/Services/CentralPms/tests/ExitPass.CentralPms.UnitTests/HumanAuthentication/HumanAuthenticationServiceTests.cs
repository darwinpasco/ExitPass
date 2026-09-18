using System.Security.Claims;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
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
    [InlineData(HumanSessionAudiences.OperatorConsole)]
    [InlineData(HumanSessionAudiences.NativeParkingApp)]
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
    public async Task Wrong_application_login_fails_closed_before_session_issuance()
    {
        var fixture = new Fixture
        {
            EffectiveRoleCodes = [ApprovedIdentityRoleCatalog.SiteOperator]
        };
        fixture.Login = fixture.CreateLogin(privileged: false);

        var result = await fixture.LoginAsync(HumanSessionAudiences.NativeParkingApp);

        result.HttpStatusCode.Should().Be(403);
        result.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_DENIED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().CreateSessionAsync(
            default, default, default!, default, default, default, default, default!, default, default,
            default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Unknown_role_policy_fails_closed_before_session_issuance()
    {
        var fixture = new Fixture { EffectiveRoleCodes = ["UNKNOWN_ROLE"] };
        fixture.Login = fixture.CreateLogin(privileged: false);

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.HttpStatusCode.Should().Be(403);
        result.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_POLICY_UNKNOWN");
    }

    [Fact]
    public async Task Unavailable_role_policy_fails_closed_before_session_issuance()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false);
        fixture.Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<EffectiveHumanAuthorization>(_ => throw new InvalidOperationException("unavailable"));

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.HttpStatusCode.Should().Be(503);
        result.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_POLICY_UNAVAILABLE");
        result.Response.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Continue_session_rechecks_application_audience_before_rotation()
    {
        var fixture = new Fixture { EffectiveRoleCodes = [ApprovedIdentityRoleCatalog.SiteOperator] };
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(
            Fixture.Session(Guid.NewGuid(), token.SessionReference, HumanSessionAudiences.NativeParkingApp,
                null, false, null, null, "PASSWORD", now, now.AddMinutes(15), now.AddHours(8),
                Guid.NewGuid(), false) with { SessionSecretHash = fixture.Tokens.HashSecret(token.Secret) });

        var result = await fixture.Service.ContinueSessionAsync(token.SerializedToken, fixture.Context(),
            CancellationToken.None);

        result.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_DENIED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().RotateSessionAsync(
            default!, default!, default, default, default, default!, default, default, default, default,
            default);
    }

    [Fact]
    public async Task Fresh_authentication_rechecks_application_audience_before_rotation()
    {
        var fixture = new Fixture { EffectiveRoleCodes = [ApprovedIdentityRoleCatalog.SiteOperator] };
        fixture.Login = fixture.CreateLogin(privileged: false);
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(
            Fixture.Session(fixture.Login.UserId, token.SessionReference, HumanSessionAudiences.NativeParkingApp,
                null, false, null, null, "PASSWORD", now, now.AddMinutes(15), now.AddHours(8),
                Guid.NewGuid(), false) with { SessionSecretHash = fixture.Tokens.HashSecret(token.Secret) });

        var result = await fixture.Service.FreshAuthenticateAsync(token.SerializedToken, "valid-password", null,
            fixture.Context(), CancellationToken.None);

        result.Response.ErrorCode.Should().Be("APPLICATION_AUDIENCE_DENIED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().RotateSessionAsync(
            default!, default!, default, default, default, default!, default, default, default, default,
            default);
    }

    [Fact]
    public async Task Operations_supervisor_operator_session_keeps_assigned_site_and_excludes_management_statutory_authority()
    {
        var fixture = new Fixture { EffectiveRoleCodes = [ApprovedIdentityRoleCatalog.OperationsSupervisor] };
        fixture.Login = fixture.CreateLogin(privileged: true);
        fixture.Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new EffectiveHumanAuthorization(
                ["shift.manage", "statutory-discounts.decision.approve"], [fixture.SiteId], [], false,
                fixture.EffectiveRoleCodes));

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.Permissions.Should().Contain("shift.manage");
        result.Response.Session.Permissions.Should().NotContain("statutory-discounts.decision.approve");
        result.Response.Session.SiteReferences.Should().Equal(fixture.SiteId);
        result.Response.Session.HasGlobalScope.Should().BeFalse();
    }

    [Fact]
    public async Task Operations_supervisor_management_session_exposes_statutory_permission_without_global_operational_scope()
    {
        var fixture = new Fixture(totpSucceeds: true)
        {
            EffectiveRoleCodes = [ApprovedIdentityRoleCatalog.OperationsSupervisor]
        };
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator);
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new EffectiveHumanAuthorization(
                ["statutory-discounts.decision.approve"], [fixture.SiteId], [], false,
                fixture.EffectiveRoleCodes));

        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform, "123456");

        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.Permissions.Should().Contain("statutory-discounts.decision.approve");
        result.Response.Session.SiteReferences.Should().Equal(fixture.SiteId);
        result.Response.Session.HasGlobalScope.Should().BeFalse();
    }

    [Fact]
    public async Task Management_platform_requires_totp_for_every_user_regardless_of_role()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false, fixture.Authenticator);

        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform);

        result.HttpStatusCode.Should().Be(401);
        result.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
    }

    [Fact]
    public async Task Expired_temporary_password_cannot_create_a_login_session()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false) with
        {
            Credential = fixture.CreateLogin(false).Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.Response.Authenticated.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("TEMPORARY_PASSWORD_EXPIRED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().CreateSessionAsync(
            default, default, default!, default, default, default, default, default!, default, default,
            default, default, default, default, default!, default);
        await fixture.Repository.DidNotReceiveWithAnyArgs().ApplyAuthenticationLockoutAsync(
            default, default, default, default!, default, default);
    }

    [Fact]
    public async Task Bootstrap_credential_with_missing_expiry_fails_closed()
    {
        var fixture = new Fixture();
        var login = fixture.CreateLogin(privileged: false);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = null
            }
        };

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.Response.Authenticated.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("TEMPORARY_PASSWORD_EXPIRED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().CreateSessionAsync(
            default, default, default!, default, default, default, default, default!, default, default,
            default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Temporary_password_is_valid_before_expiry_and_rejected_at_exact_expiry()
    {
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var beforeExpiry = new Fixture(timeProvider: new FixedTimeProvider(now));
        var beforeLogin = beforeExpiry.CreateLogin(privileged: false);
        beforeExpiry.Login = beforeLogin with
        {
            Credential = beforeLogin.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = now.AddTicks(1)
            }
        };
        var atExpiry = new Fixture(timeProvider: new FixedTimeProvider(now));
        var expiredLogin = atExpiry.CreateLogin(privileged: false);
        atExpiry.Login = expiredLogin with
        {
            Credential = expiredLogin.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = now
            }
        };

        var accepted = await beforeExpiry.LoginAsync(HumanSessionAudiences.OperatorConsole);
        var rejected = await atExpiry.LoginAsync(HumanSessionAudiences.OperatorConsole);

        accepted.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.PasswordChangeRequired);
        rejected.Response.ErrorCode.Should().Be("TEMPORARY_PASSWORD_EXPIRED");
    }

    [Theory]
    [InlineData(HumanSessionAudiences.OperatorConsole)]
    [InlineData(HumanSessionAudiences.NativeParkingApp)]
    public async Task Valid_temporary_password_creates_only_a_password_change_required_session(string audience)
    {
        var fixture = new Fixture();
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            }
        };

        var result = await fixture.LoginAsync(audience);

        result.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.PasswordChangeRequired);
        result.Response.Session!.PasswordChangeRequired.Should().BeTrue();
        result.Response.Session.Permissions.Should().BeEmpty();
        result.Response.Session.SiteReferences.Should().BeEmpty();
        result.Response.Session.SiteGroupReferences.Should().BeEmpty();
        result.Response.Session.HasGlobalScope.Should().BeFalse();
    }

    [Fact]
    public async Task Password_change_required_session_never_acquires_normal_authority_when_policy_is_unavailable()
    {
        var fixture = new Fixture();
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            }
        };
        fixture.Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<EffectiveHumanAuthorization>(_ => throw new InvalidOperationException("unavailable"));

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.Response.Outcome.Should().Be(HumanAuthenticationOutcomes.PasswordChangeRequired);
        result.Response.Session!.Permissions.Should().BeEmpty();
        result.Response.Session.SiteReferences.Should().BeEmpty();
        result.Response.Session.SiteGroupReferences.Should().BeEmpty();
        result.Response.Session.HasGlobalScope.Should().BeFalse();
    }

    [Theory]
    [InlineData(HumanSessionAudiences.OperatorConsole)]
    [InlineData(HumanSessionAudiences.NativeParkingApp)]
    public async Task Password_change_from_operational_session_requires_totp(string audience)
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(
            Fixture.Session(fixture.Login.UserId, token.SessionReference, audience,
                null, false, null, null, "PASSWORD", now, now.AddMinutes(15), now.AddHours(8), Guid.NewGuid(), false)
            with { SessionSecretHash = fixture.Tokens.HashSecret(token.Secret) });

        var result = await fixture.Service.ChangePasswordAsync(token.SerializedToken, "valid-password",
            "replacement horse battery staple", null, fixture.Context(), CancellationToken.None);

        result.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
    }

    [Theory]
    [InlineData("ACTIVE", HumanSessionAudiences.OperatorConsole)]
    [InlineData("CHANGE_REQUIRED", HumanSessionAudiences.OperatorConsole)]
    [InlineData("ACTIVE", HumanSessionAudiences.NativeParkingApp)]
    [InlineData("CHANGE_REQUIRED", HumanSessionAudiences.NativeParkingApp)]
    public async Task Successful_password_change_revokes_sessions_and_requires_a_new_login(
        string credentialStatus,
        string audience)
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = credentialStatus,
                TemporaryPasswordExpiresAt = credentialStatus == "CHANGE_REQUIRED"
                    ? DateTimeOffset.UtcNow.AddHours(1)
                    : null
            }
        };
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(
            Fixture.Session(fixture.Login.UserId, token.SessionReference, audience,
                null, false, null, null, credentialStatus == "CHANGE_REQUIRED" ? "PASSWORD_CHANGE_REQUIRED" : "PASSWORD",
                now, now.AddMinutes(15), now.AddHours(8), Guid.NewGuid(), false) with
            {
                SessionSecretHash = fixture.Tokens.HashSecret(token.Secret),
                LocalCredentialStatus = credentialStatus
            });

        var result = await fixture.Service.ChangePasswordAsync(token.SerializedToken, "valid-password",
            "replacement horse battery staple", "123456", fixture.Context(), CancellationToken.None);

        result.HttpStatusCode.Should().Be(200);
        result.Response.Authenticated.Should().BeFalse();
        result.Credential.Should().BeNull();
        await fixture.Repository.Received(1).ChangePasswordAsync(fixture.Login.UserId,
            fixture.Login.Credential!.LocalCredentialId, fixture.Login.Credential.RowVersion,
            Arg.Any<PasswordHashMaterial>(), Arg.Any<DateTimeOffset>(), fixture.Login.UserId,
            Arg.Any<CancellationToken>());
        await fixture.Repository.DidNotReceiveWithAnyArgs().ChangePasswordAndRotateSessionAsync(
            default!, default, default!, default!, default, default, default, default!, default, default,
            default, default, default);
    }

    [Theory]
    [InlineData("ACTIVE", HumanSessionAudiences.ManagementPlatform)]
    [InlineData("CHANGE_REQUIRED", HumanSessionAudiences.ManagementPlatform)]
    [InlineData("ACTIVE", HumanSessionAudiences.OperatorConsole)]
    [InlineData("CHANGE_REQUIRED", HumanSessionAudiences.OperatorConsole)]
    public async Task Password_change_rejects_seven_character_password_for_every_credential_status_and_audience(
        string credentialStatus, string audience)
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with { Credential = login.Credential! with { Status = credentialStatus } };
        fixture.Passwords.HashAsync("1234567", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PasswordHashMaterial>(new ArgumentException("Password too short.")));
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var token = fixture.Tokens.Create();
        var now = DateTimeOffset.UtcNow;
        fixture.Repository.FindSessionAsync(token.SessionReference, Arg.Any<CancellationToken>()).Returns(
            Fixture.Session(login.UserId, token.SessionReference, audience, null, false, null, null,
                "PASSWORD", now, now.AddMinutes(15), now.AddHours(8), Guid.NewGuid(), false) with
            {
                SessionSecretHash = fixture.Tokens.HashSecret(token.Secret),
                LocalCredentialStatus = credentialStatus
            });

        var result = await fixture.Service.ChangePasswordAsync(token.SerializedToken, "valid-password",
            "1234567", "123456", fixture.Context(), CancellationToken.None);

        result.HttpStatusCode.Should().Be(400);
        result.Response.ErrorCode.Should().Be("PASSWORD_POLICY_FAILED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().ChangePasswordAsync(
            default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Active_user_can_reset_password_with_totp_without_current_password()
    {
        var fixture = new Fixture(totpSucceeds: true);
        fixture.Login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", null, "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        result.HttpStatusCode.Should().Be(200);
        result.Response.Outcome.Should().Be("PASSWORD_RESET_COMPLETED");
        await fixture.Repository.Received(1).ChangePasswordAsync(fixture.Login.UserId,
            fixture.Login.Credential!.LocalCredentialId, fixture.Login.Credential.RowVersion,
            Arg.Any<PasswordHashMaterial>(), Arg.Any<DateTimeOffset>(), fixture.Login.UserId,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("CHANGE_REQUIRED")]
    public async Task Totp_password_recovery_rejects_seven_character_password(string credentialStatus)
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = credentialStatus,
                TemporaryPasswordExpiresAt = credentialStatus == "CHANGE_REQUIRED"
                    ? DateTimeOffset.UtcNow.AddMinutes(-1)
                    : null
            }
        };
        fixture.Passwords.HashAsync("1234567", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PasswordHashMaterial>(new ArgumentException("Password too short.")));
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01",
            credentialStatus == "CHANGE_REQUIRED" ? "valid-password" : null,
            "123456", "1234567", fixture.Context(), CancellationToken.None);

        result.HttpStatusCode.Should().Be(400);
        result.Response.ErrorCode.Should().Be("PASSWORD_POLICY_FAILED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().ChangePasswordAsync(
            default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Active_user_reset_ignores_arbitrary_expired_password_field_but_still_requires_totp()
    {
        var fixture = new Fixture(totpSucceeds: true);
        fixture.Login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var withoutTotp = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "arbitrary-value", "",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);
        var withTotp = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "arbitrary-value", "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        withoutTotp.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
        withTotp.HttpStatusCode.Should().Be(200);
        await fixture.Passwords.DidNotReceive().VerifyAsync("arbitrary-value",
            Arg.Any<LocalCredentialRecord?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expired_bootstrap_recovery_requires_expired_password_and_totp()
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var missingPassword = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", null, "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);
        var success = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "valid-password", "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        missingPassword.Response.ErrorCode.Should().Be("EXPIRED_TEMPORARY_PASSWORD_REQUIRED");
        success.HttpStatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Expired_bootstrap_recovery_rejects_wrong_password_and_missing_totp()
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };
        fixture.Passwords.VerifyAsync("wrong-bootstrap", Arg.Any<LocalCredentialRecord?>(),
            Arg.Any<CancellationToken>()).Returns(false);

        var wrongPassword = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "wrong-bootstrap", "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);
        var missingTotp = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "valid-password", "",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        wrongPassword.Response.ErrorCode.Should().Be("EXPIRED_TEMPORARY_PASSWORD_REQUIRED");
        missingTotp.Response.ErrorCode.Should().Be("TOTP_REQUIRED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().ChangePasswordAsync(
            default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Unexpired_bootstrap_credential_cannot_use_forgot_password_flow()
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
            }
        };

        var result = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "valid-password", "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        result.Response.ErrorCode.Should().Be("RESET_AUTHENTICATION_FAILED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().ChangePasswordAsync(
            default, default, default, default!, default, default, default);
    }

    [Theory]
    [InlineData("PENDING_ACTIVATION", "ACTIVE")]
    [InlineData("ACTIVE", "LOCKED")]
    public async Task Legacy_activation_or_non_active_account_cannot_use_totp_reset(
        string credentialStatus,
        string userStatus)
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            UserStatus = userStatus,
            Credential = login.Credential! with { Status = credentialStatus }
        };

        var result = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", null, "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        result.Response.ErrorCode.Should().Be("RESET_AUTHENTICATION_FAILED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().ChangePasswordAsync(
            default, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task Successful_expired_bootstrap_recovery_invalidates_old_password_and_totp_replay()
    {
        var fixture = new Fixture(totpSucceeds: true);
        var login = fixture.CreateLogin(privileged: false, fixture.Authenticator);
        fixture.Login = login with
        {
            Credential = login.Credential! with
            {
                Status = "CHANGE_REQUIRED",
                TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };
        fixture.Repository.TryRecordTotpSuccessAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true, false);

        var recovered = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "valid-password", "123456",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);
        fixture.Login = fixture.Login with
        {
            Credential = fixture.Login.Credential! with
            {
                Status = "ACTIVE",
                TemporaryPasswordExpiresAt = null
            }
        };
        fixture.Passwords.VerifyAsync("valid-password", Arg.Any<LocalCredentialRecord?>(),
            Arg.Any<CancellationToken>()).Returns(false);
        var oldPasswordLogin = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);
        var replayedReset = await fixture.Service.ResetPasswordWithTotpAsync("Cashier01", "valid-password", "123456",
            "another replacement battery staple", fixture.Context(), CancellationToken.None);

        recovered.HttpStatusCode.Should().Be(200);
        oldPasswordLogin.Response.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        replayedReset.Response.ErrorCode.Should().Be("TOTP_INVALID");
        await fixture.Repository.Received(1).ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<PasswordHashMaterial>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Operator_console_user_authenticates_without_site_shift_schedule_or_custody()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: false);
        fixture.Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new EffectiveHumanAuthorization([], [], [], false,
                [ApprovedIdentityRoleCatalog.SiteOperator]));

        var result = await fixture.LoginAsync(HumanSessionAudiences.OperatorConsole);

        result.Response.Authenticated.Should().BeTrue();
        result.Response.Session!.SiteReferences.Should().BeEmpty();
        result.Response.Session.SiteGroupReferences.Should().BeEmpty();
    }

    [Fact]
    public async Task Management_user_without_authenticator_fails_closed()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true);
        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform);
        result.HttpStatusCode.Should().Be(403);
        result.Response.Authenticated.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("TOTP_AUTHENTICATOR_UNAVAILABLE");
    }

    [Theory]
    [InlineData("PENDING_ENROLLMENT")]
    [InlineData("RESET_REQUIRED")]
    public async Task Management_user_with_non_active_totp_fails_closed(string status)
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator with { Status = status });

        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform);

        result.HttpStatusCode.Should().Be(403);
        result.Response.Authenticated.Should().BeFalse();
        result.Response.ErrorCode.Should().Be("TOTP_AUTHENTICATOR_UNAVAILABLE");
    }

    [Theory]
    [InlineData("SUSPENDED", "MFA_UNAVAILABLE", 403)]
    [InlineData("ACTIVE", "TOTP_PROTECTION_UNAVAILABLE", 503)]
    public async Task Privileged_management_user_with_unusable_totp_fails_closed(string status, string errorCode, int statusCode)
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(privileged: true, fixture.Authenticator with { Status = status });
        if (status == "ACTIVE") fixture.Protector.IsConfigured.Returns(false);

        var result = await fixture.LoginAsync(HumanSessionAudiences.ManagementPlatform, "123456");

        result.HttpStatusCode.Should().Be(statusCode);
        result.Response.ErrorCode.Should().Be(errorCode);
        result.Response.Authenticated.Should().BeFalse();
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

    [Fact]
    public async Task Legacy_password_reset_request_never_issues_or_delivers_email_material()
    {
        var fixture = new Fixture();
        fixture.Login = fixture.CreateLogin(false) with { Email = "employee@example.test" };
        fixture.ChallengeDelivery.Enabled.Returns(true);

        await fixture.Service.RequestPasswordResetAsync("Cashier01", fixture.Context(), CancellationToken.None);

        await fixture.Repository.DidNotReceiveWithAnyArgs().CreateCredentialChallengeAsync(
            default, default!, default!, default, default, default, default, default);
        await fixture.ChallengeDelivery.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default);
    }

    [Fact]
    public async Task Legacy_bearer_challenge_mutation_flows_are_disabled()
    {
        var fixture = new Fixture();
        var reset = await fixture.Service.ResetPasswordAsync(Guid.NewGuid(), "secret",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);
        var activation = await fixture.Service.ActivateAsync(Guid.NewGuid(), "secret",
            "replacement horse battery staple", fixture.Context(), CancellationToken.None);

        reset.HttpStatusCode.Should().Be(410);
        reset.Response.ErrorCode.Should().Be("TOTP_RESET_REQUIRED");
        activation.HttpStatusCode.Should().Be(410);
        activation.Response.ErrorCode.Should().Be("TEMPORARY_PASSWORD_LOGIN_REQUIRED");
        await fixture.Repository.DidNotReceiveWithAnyArgs().CompleteCredentialChallengeAsync(
            default, default!, default!, default!, default!, default, default!, default);
    }

    [Theory]
    [InlineData(HumanSessionAudiences.ManagementPlatform, "/v1/management-platform/identity/users")]
    [InlineData(HumanSessionAudiences.OperatorConsole, "/v1/operator-console/business-action")]
    [InlineData(HumanSessionAudiences.Apt, "/v1/apt/payable-basis/resolve")]
    [InlineData(HumanSessionAudiences.NativeParkingApp, "/v1/native-parking/business-action")]
    public async Task Password_change_required_session_is_blocked_from_every_business_audience(
        string audience,
        string path)
    {
        var nextCalled = false;
        var middleware = new CentralPmsRbacMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = RestrictedSessionContext(audience, path, passwordChangeRequired: true, mfaSatisfied: true);

        await middleware.InvokeAsync(context, Options.Create(new CentralPmsRbacOptions { Enabled = false }),
            Substitute.For<ICentralPmsRbacRepository>(), TestEnvironment(),
            NullLogger<CentralPmsRbacMiddleware>.Instance);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData(HumanSessionAudiences.NativeParkingApp, "/v1/human-authentication/password/change")]
    [InlineData(HumanSessionAudiences.OperatorConsole, "/v1/human-authentication/logout")]
    [InlineData(HumanSessionAudiences.Apt, "/v1/apt/human-sessions/11111111-1111-1111-1111-111111111111/password/change")]
    public async Task Password_change_required_session_can_reach_only_authentication_lifecycle_routes(
        string audience,
        string path)
    {
        var nextCalled = false;
        var middleware = new CentralPmsRbacMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = RestrictedSessionContext(audience, path, passwordChangeRequired: true, mfaSatisfied: true);

        await middleware.InvokeAsync(context, Options.Create(new CentralPmsRbacOptions { Enabled = false }),
            Substitute.For<ICentralPmsRbacRepository>(), TestEnvironment(),
            NullLogger<CentralPmsRbacMiddleware>.Instance);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Management_session_without_mfa_is_blocked_even_for_non_privileged_user()
    {
        var nextCalled = false;
        var middleware = new CentralPmsRbacMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = RestrictedSessionContext(HumanSessionAudiences.ManagementPlatform,
            "/v1/management-platform/identity/users", passwordChangeRequired: false, mfaSatisfied: false);

        await middleware.InvokeAsync(context, Options.Create(new CentralPmsRbacOptions { Enabled = false }),
            Substitute.For<ICentralPmsRbacRepository>(), TestEnvironment(),
            NullLogger<CentralPmsRbacMiddleware>.Instance);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static DefaultHttpContext RestrictedSessionContext(
        string audience,
        string path,
        bool passwordChangeRequired,
        bool mfaSatisfied)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim("exitpass_audience", audience),
            new Claim("password_change_required", passwordChangeRequired ? "true" : "false"),
            new Claim("mfa_satisfied", mfaSatisfied ? "true" : "false")
        ], HumanSessionAuthenticationHandler.SchemeName));
        return context;
    }

    private static IWebHostEnvironment TestEnvironment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        return environment;
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
        public IReadOnlyList<string> EffectiveRoleCodes { get; set; } = ApprovedIdentityRoleCatalog.AssignableCodes;
        public HumanAuthenticationService Service { get; }

        public Fixture(bool passwordSucceeds = true, bool totpSucceeds = false, TimeProvider? timeProvider = null)
        {
            var options = Options.Create(new HumanAuthenticationOptions());
            Passwords.VerifyAsync(Arg.Any<string>(), Arg.Any<LocalCredentialRecord?>(), Arg.Any<CancellationToken>()).Returns(passwordSucceeds);
            Passwords.HashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
                new PasswordHashMaterial(new byte[32], new byte[16], "ARGON2ID", 19, 3, 65536, 1));
            Passwords.NeedsUpgrade(Arg.Any<LocalCredentialRecord>()).Returns(false);
            Protector.IsConfigured.Returns(true);
            ChallengeDelivery.Enabled.Returns(false);
            Protector.Unprotect(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<TotpAuthenticatorRecord>()).Returns(new byte[20]);
            Totp.Verify(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>()).Returns(new TotpVerificationResult(totpSucceeds, totpSucceeds ? 123 : null));
            Repository.FindLocalLoginAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => Login);
            Repository.CountRecentFailedAttemptsAsync(Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(0);
            Repository.GetEffectiveAuthorizationAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(_ => new EffectiveHumanAuthorization(["test.permission"], [SiteId], [], false,
                    EffectiveRoleCodes));
            Repository.CreateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<SessionCredential>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var token = call.ArgAt<SessionCredential>(14);
                    var record = Session(call.ArgAt<Guid>(0), token.SessionReference, call.ArgAt<string>(2), call.ArgAt<Guid?>(3), call.ArgAt<bool>(4), call.ArgAt<Guid?>(5), call.ArgAt<DateTimeOffset?>(6), call.ArgAt<string>(7), call.ArgAt<DateTimeOffset>(10), call.ArgAt<DateTimeOffset>(11), call.ArgAt<DateTimeOffset>(12), call.ArgAt<Guid>(13), Login?.HasPrivilegedRole ?? false);
                    return new SessionIssue(record.HumanSessionId, token, record);
                });
            Service = new HumanAuthenticationService(Repository, Passwords, Totp, Protector, Tokens, ChallengeDelivery,
                timeProvider ?? TimeProvider.System, options);
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
