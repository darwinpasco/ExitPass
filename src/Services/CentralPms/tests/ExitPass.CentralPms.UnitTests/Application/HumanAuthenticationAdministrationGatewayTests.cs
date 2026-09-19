using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class HumanAuthenticationAdministrationGatewayTests
{
    private static readonly IdentityAdministrationActor Actor = new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task RevokeSession_ResolvesOpaqueReferenceAndUsesInternalI020SessionIdentity()
    {
        var fixture = new Fixture();
        var publicReference = Guid.NewGuid();
        var internalSessionId = Guid.NewGuid();
        var targetUser = Guid.NewGuid();
        fixture.Authentication.FindSessionAsync(publicReference, Arg.Any<CancellationToken>())
            .Returns(Session(internalSessionId, publicReference, targetUser));

        var result = await fixture.Gateway.RevokeSessionsAsync(Actor,
            new(targetUser, publicReference, "SECURITY_RESPONSE", Guid.NewGuid()), CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        await fixture.Authentication.Received(1).RevokeSessionAdministrativelyAsync(internalSessionId, targetUser,
            Actor.UserId, "SECURITY_RESPONSE", result.CorrelationId, fixture.Options.CentralPmsServiceIdentityId,
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeSession_AntiEnumeratesCrossUserReference()
    {
        var fixture = new Fixture();
        var publicReference = Guid.NewGuid();
        fixture.Authentication.FindSessionAsync(publicReference, Arg.Any<CancellationToken>())
            .Returns(Session(Guid.NewGuid(), publicReference, Guid.NewGuid()));

        var result = await fixture.Gateway.RevokeSessionsAsync(Actor,
            new(Guid.NewGuid(), publicReference, "SECURITY_RESPONSE", Guid.NewGuid()), CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.NotFound);
        await fixture.Authentication.DidNotReceiveWithAnyArgs().RevokeSessionAdministrativelyAsync(
            default, default, default, default!, default, default, default, default);
    }

    [Theory]
    [InlineData("SETUP", null)]
    [InlineData("RESET", 7L)]
    public async Task ProvisionMfa_ReturnsOneTimeMaterialAndCurrentSafeStatus(string action, long? rowVersion)
    {
        var fixture = new Fixture();
        var targetUser = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var status = new IdentityMfaStatus(true, true, "ACTIVE", null, DateTimeOffset.UtcNow, null,
            null, null, 1);
        fixture.Identity.GetUserAsync(Actor, targetUser, correlation, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityUserDetail>.Succeeded(UserDetail(targetUser), correlation));
        fixture.Mfa.ProvisionTotpAsync(targetUser, "target.user", rowVersion, action, Actor.UserId,
                "SECURITY_RESPONSE", correlation, Arg.Any<CancellationToken>())
            .Returns(new AdminTotpProvisioningMaterial("NEW-BASE32-SECRET", "otpauth://totp/ExitPass:target.user"));
        fixture.Identity.GetMfaStatusAsync(Actor, targetUser, correlation, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityMfaStatus>.Succeeded(status, correlation));

        var result = await fixture.Gateway.ProvisionMfaAsync(Actor,
            new(targetUser, action, rowVersion, "SECURITY_RESPONSE", correlation), CancellationToken.None);

        result.Value!.MfaStatus.Should().Be(status);
        result.Value.Provisioning.TotpSharedSecret.Should().Be("NEW-BASE32-SECRET");
        result.Value.Provisioning.TotpProvisioningUri.Should().StartWith("otpauth://");
        result.Value.Provisioning.DisplayOnce.Should().BeTrue();
        await fixture.Mfa.Received(1).ProvisionTotpAsync(targetUser, "target.user", rowVersion, action, Actor.UserId,
            "SECURITY_RESPONSE", correlation, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMfa_ReturnsOnlySafeRepositoryStatus()
    {
        var fixture = new Fixture();
        var targetUser = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var status = new IdentityMfaStatus(true, false, "REVOKED", null, null, null, null,
            DateTimeOffset.UtcNow, 8);
        fixture.Mfa.RemoveTotpAsync(targetUser, 7, Actor.UserId, "SECURITY_RESPONSE", correlation,
                Arg.Any<CancellationToken>()).Returns(true);
        fixture.Identity.GetMfaStatusAsync(Actor, targetUser, correlation, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityMfaStatus>.Succeeded(status, correlation));

        var result = await fixture.Gateway.RemoveMfaAsync(Actor,
            new(targetUser, 7, "SECURITY_RESPONSE", correlation), CancellationToken.None);

        result.Value.Should().Be(status);
        await fixture.Mfa.Received(1).RemoveTotpAsync(targetUser, 7, Actor.UserId,
            "SECURITY_RESPONSE", correlation, Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public readonly IHumanAuthenticationRepository Authentication = Substitute.For<IHumanAuthenticationRepository>();
        public readonly IHumanMfaAdministrationService Mfa = Substitute.For<IHumanMfaAdministrationService>();
        public readonly ICredentialChallengeDelivery Delivery = Substitute.For<ICredentialChallengeDelivery>();
        public readonly ICredentialChallengeLinkBuilder Links = Substitute.For<ICredentialChallengeLinkBuilder>();
        public readonly IManagementPlatformIdentityAdministrationRepository Identity = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        public readonly HumanAuthenticationOptions Options = new();
        public readonly HumanAuthenticationAdministrationGateway Gateway;

        public Fixture()
        {
            Gateway = new HumanAuthenticationAdministrationGateway(Authentication, Mfa, Delivery, Links, Identity,
                Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        }
    }

    private static HumanSessionRecord Session(Guid internalId, Guid publicReference, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return new(internalId, publicReference, "hash", userId, "user", "User", "ACTIVE",
            now.AddDays(-1), null, null, "LOCAL", Guid.NewGuid(), "ACTIVE", null,
            "MANAGEMENT_PLATFORM", null, "ACTIVE", "PASSWORD_TOTP", true, Guid.NewGuid(), now,
            now, now, now.AddMinutes(15), now.AddHours(8), 1, 1, 1, 1, true, Guid.NewGuid(), 1);
    }

    private static IdentityUserDetail UserDetail(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return new(new(userId, "target.user", "Target User", null, null, "SYSTEM_ADMINISTRATOR",
            "ACTIVE", now.AddDays(-1), null, null, 1), [], []);
    }
}
