using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using FluentAssertions;
using Microsoft.Extensions.Options;
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
    [InlineData("RESET")]
    [InlineData("REMOVE")]
    public async Task ChangeMfa_UsesI020PrimitiveAndReturnsSafeRepositoryStatus(string action)
    {
        var fixture = new Fixture();
        var targetUser = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var status = new IdentityMfaStatus(true, action == "RESET", action == "RESET" ? "RESET_REQUIRED" : "REVOKED",
            null, null, null, action == "RESET" ? DateTimeOffset.UtcNow : null,
            action == "REMOVE" ? DateTimeOffset.UtcNow : null, 8);
        fixture.Mfa.ChangeTotpAsync(targetUser, 7, action, Actor.UserId, "SECURITY_RESPONSE", correlation,
                Arg.Any<CancellationToken>()).Returns(true);
        fixture.Identity.GetMfaStatusAsync(Actor, targetUser, correlation, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityMfaStatus>.Succeeded(status, correlation));

        var result = await fixture.Gateway.ChangeMfaAsync(Actor,
            new(targetUser, action, 7, "SECURITY_RESPONSE", correlation), CancellationToken.None);

        result.Value.Should().Be(status);
        await fixture.Mfa.Received(1).ChangeTotpAsync(targetUser, 7, action, Actor.UserId,
            "SECURITY_RESPONSE", correlation, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CredentialChallenge_WhenDeliveryIsDisabled_DoesNotCreateStrandedSecret()
    {
        var fixture = new Fixture(challengeDeliveryEnabled: false);
        var command = new CreateCredentialResetChallengeCommand(Guid.NewGuid(), "PASSWORD_RESET",
            DateTimeOffset.UtcNow.AddMinutes(10), "ADMIN_RESET", Guid.NewGuid());

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor, command, CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.IntegrationUnavailable);
        result.Classification.Should().Be("CREDENTIAL_CHALLENGE_DELIVERY_NOT_CONFIGURED");
        await fixture.Authentication.DidNotReceiveWithAnyArgs().CreateCredentialChallengeAsync(default, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task CredentialChallenge_DeliversSecretButReturnsOnlyOpaqueReference()
    {
        var fixture = new Fixture(challengeDeliveryEnabled: true);
        var reference = Guid.NewGuid();
        var command = new CreateCredentialResetChallengeCommand(Guid.NewGuid(), "PASSWORD_RESET",
            DateTimeOffset.UtcNow.AddMinutes(10), "ADMIN_RESET", Guid.NewGuid());
        fixture.Authentication.CreateCredentialChallengeAsync(command.UserReference, command.Purpose,
                Arg.Any<DateTimeOffset>(), command.ExpiresAt, fixture.Options.CentralPmsServiceIdentityId,
                command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns((reference, "one-time-delivery-secret"));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor, command, CancellationToken.None);

        result.Value.Should().Be(new CredentialResetChallengeResult(reference, command.ExpiresAt));
        result.Value!.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        await fixture.Delivery.Received(1).DeliverAsync(
            Arg.Is<CredentialChallengeDeliveryRequest>(request => request.ChallengeReference == reference),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public readonly IHumanAuthenticationRepository Authentication = Substitute.For<IHumanAuthenticationRepository>();
        public readonly IHumanMfaAdministrationService Mfa = Substitute.For<IHumanMfaAdministrationService>();
        public readonly ICredentialChallengeDelivery Delivery = Substitute.For<ICredentialChallengeDelivery>();
        public readonly IManagementPlatformIdentityAdministrationRepository Identity = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        public readonly HumanAuthenticationOptions Options = new();
        public readonly HumanAuthenticationAdministrationGateway Gateway;

        public Fixture(bool challengeDeliveryEnabled = false)
        {
            Delivery.Enabled.Returns(challengeDeliveryEnabled);
            Gateway = new HumanAuthenticationAdministrationGateway(Authentication, Mfa, Delivery, Identity,
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
}
