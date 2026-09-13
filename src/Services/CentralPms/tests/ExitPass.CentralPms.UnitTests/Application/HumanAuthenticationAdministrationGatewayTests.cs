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
        result.Classification.Should().Be("CREDENTIAL_CHALLENGE_EMAIL_DELIVERY_NOT_CONFIGURED");
        await fixture.Authentication.DidNotReceiveWithAnyArgs().CreateCredentialChallengeAsync(default, default!, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task CredentialChallenge_DeliversSecretButReturnsOnlyOpaqueReference()
    {
        var fixture = new Fixture(challengeDeliveryEnabled: true);
        var reference = Guid.NewGuid();
        var command = new CreateCredentialResetChallengeCommand(Guid.NewGuid(), "PASSWORD_RESET",
            DateTimeOffset.UtcNow.AddMinutes(10), "ADMIN_RESET", Guid.NewGuid());
        fixture.Authentication.CreateCredentialChallengeAsync(command.UserReference, command.Purpose,
                "PASSWORD_RESET_EMAIL", Arg.Any<DateTimeOffset>(), command.ExpiresAt, fixture.Options.CentralPmsServiceIdentityId,
                command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns((reference, "one-time-delivery-secret"));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor, command, CancellationToken.None);

        result.Value.Should().Be(new CredentialResetChallengeResult(reference, command.ExpiresAt));
        result.Value!.OneTimeActivation.Should().BeNull();
        await fixture.Delivery.Received(1).DeliverAsync(
            Arg.Is<CredentialChallengeDeliveryRequest>(request => request.ChallengeReference == reference &&
                request.RecipientEmail == "employee@example.test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountActivation_WhenEmailDeliveryFails_RevokesChallengeAndReturnsControlledFailure()
    {
        var fixture = new Fixture(challengeDeliveryEnabled: true);
        var reference = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new CreateCredentialResetChallengeCommand(userId, "ACCOUNT_ACTIVATION",
            DateTimeOffset.UtcNow.AddMinutes(10), "ONBOARDING", Guid.NewGuid());
        fixture.Authentication.GetCredentialChallengeTargetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new CredentialChallengeTarget(userId, "INVITED", "employee@example.test", 0));
        fixture.Authentication.CreateCredentialChallengeAsync(userId, "ACCOUNT_ACTIVATION",
                "ACCOUNT_ACTIVATION_EMAIL", Arg.Any<DateTimeOffset>(), command.ExpiresAt,
                fixture.Options.CentralPmsServiceIdentityId, command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns((reference, "task-owned-delivery-material"));
        fixture.Delivery.DeliverAsync(Arg.Any<CredentialChallengeDeliveryRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("test SMTP failure")));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor, command, CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.IntegrationUnavailable);
        result.Classification.Should().Be("CREDENTIAL_CHALLENGE_DELIVERY_FAILED");
        result.Value.Should().BeNull();
        await fixture.Authentication.Received(1).RevokeCredentialChallengeAsync(reference,
            fixture.Options.CentralPmsServiceIdentityId, "ACCOUNT_ACTIVATION_EMAIL_DELIVERY_FAILED",
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await fixture.Authentication.Received(1).RecordSecurityEventAsync(
            "CREDENTIAL_CHALLENGE_DELIVERY_FAILED", "FAILED", "ACCOUNT_ACTIVATION_EMAIL_DELIVERY_FAILED",
            reference, Actor.UserId, null, null, command.CorrelationId,
            fixture.Options.CentralPmsServiceIdentityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountActivation_AdminIssued_ReturnsSecretOnceWithoutInvokingEmailDelivery()
    {
        var fixture = new Fixture();
        var reference = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new CreateCredentialResetChallengeCommand(userId, "ACCOUNT_ACTIVATION",
            DateTimeOffset.UtcNow.AddMinutes(10), "ONBOARDING", Guid.NewGuid(),
            ActivationDeliveryModes.AdminIssued, true);
        fixture.Authentication.GetCredentialChallengeTargetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new CredentialChallengeTarget(userId, "INVITED", null, 0));
        fixture.Authentication.CreateCredentialChallengeAsync(userId, "ACCOUNT_ACTIVATION",
                "ACCOUNT_ACTIVATION_ADMIN_ISSUED", Arg.Any<DateTimeOffset>(), command.ExpiresAt,
                fixture.Options.CentralPmsServiceIdentityId, command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns((reference, "one-time-activation-secret"));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor, command, CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        result.Value!.DeliveryMode.Should().Be(ActivationDeliveryModes.AdminIssued);
        result.Value.OneTimeActivation.Should().BeEquivalentTo(new OneTimeActivationMaterial(reference,
            "one-time-activation-secret", command.ExpiresAt,
            $"https://accounts.example.test/account/activate?challengeReference={reference:D}&challengeSecret=one-time-activation-secret",
            $"https://accounts.example.test/account/activate?challengeReference={reference:D}&challengeSecret=one-time-activation-secret"));
        await fixture.Delivery.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default);
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("LOCKED")]
    public async Task PasswordReset_AdminIssued_ForEligibleNoEmailTarget_ReturnsOneTimeCanonicalMaterial(string status)
    {
        var fixture = new Fixture();
        var reference = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var command = new CreateCredentialResetChallengeCommand(userId, "PASSWORD_RESET", expiresAt,
            "NO_EMAIL_RECOVERY", Guid.NewGuid(), ActivationDeliveryModes.AdminIssued, true);
        fixture.Authentication.GetCredentialChallengeTargetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new CredentialChallengeTarget(userId, status, null, 1));
        fixture.Authentication.CreateCredentialChallengeAsync(userId, "PASSWORD_RESET",
                "PASSWORD_RESET_ADMIN_ISSUED", Arg.Any<DateTimeOffset>(), expiresAt,
                fixture.Options.CentralPmsServiceIdentityId, command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns((reference, "one-time-reset-secret"));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor, command, CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        result.Value!.OneTimeActivation.Should().BeNull();
        result.Value.OneTimeCredential.Should().BeEquivalentTo(new OneTimeCredentialMaterial(reference,
            "one-time-reset-secret", expiresAt,
            $"https://accounts.example.test/account/reset-password?challengeReference={reference:D}&challengeSecret=one-time-reset-secret",
            $"https://accounts.example.test/account/reset-password?challengeReference={reference:D}&challengeSecret=one-time-reset-secret"));
        await fixture.Delivery.DidNotReceiveWithAnyArgs().DeliverAsync(default!, default);
        await fixture.Authentication.Received(1).RecordSecurityEventAsync("CREDENTIAL_RESET", "ALLOWED",
            "NO_EMAIL_RECOVERY", userId, Actor.UserId, null, null, command.CorrelationId,
            fixture.Options.CentralPmsServiceIdentityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ACTIVE", "employee@example.test", 1, "ADMIN_ISSUED_PASSWORD_RECOVERY_NOT_REQUIRED")]
    [InlineData("INVITED", null, 1, "PASSWORD_RESET_ACCOUNT_NOT_ELIGIBLE")]
    [InlineData("INACTIVE", null, 1, "PASSWORD_RESET_ACCOUNT_NOT_ELIGIBLE")]
    [InlineData("SUSPENDED", null, 1, "PASSWORD_RESET_ACCOUNT_NOT_ELIGIBLE")]
    [InlineData("RETIRED", null, 1, "PASSWORD_RESET_ACCOUNT_NOT_ELIGIBLE")]
    [InlineData("ACTIVE", null, 0, "PASSWORD_RESET_CREDENTIAL_CONFLICT")]
    [InlineData("LOCKED", null, 2, "PASSWORD_RESET_CREDENTIAL_CONFLICT")]
    public async Task PasswordReset_AdminIssued_RejectsIneligibleTargets(
        string status, string? email, int credentialCount, string classification)
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        fixture.Authentication.GetCredentialChallengeTargetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new CredentialChallengeTarget(userId, status, email, credentialCount));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor,
            new(userId, "PASSWORD_RESET", DateTimeOffset.UtcNow.AddMinutes(10), "NO_EMAIL_RECOVERY",
                Guid.NewGuid(), ActivationDeliveryModes.AdminIssued, true), CancellationToken.None);

        result.Classification.Should().Be(classification);
        await fixture.Authentication.DidNotReceiveWithAnyArgs().CreateCredentialChallengeAsync(
            default, default!, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task PasswordReset_AdminIssued_RequiresExplicitHandoffAcknowledgement()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();
        fixture.Authentication.GetCredentialChallengeTargetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new CredentialChallengeTarget(userId, "ACTIVE", null, 1));

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor,
            new(userId, "PASSWORD_RESET", DateTimeOffset.UtcNow.AddMinutes(10), "NO_EMAIL_RECOVERY",
                Guid.NewGuid(), ActivationDeliveryModes.AdminIssued, false), CancellationToken.None);

        result.Classification.Should().Be("ADMIN_ISSUED_HANDOFF_ACKNOWLEDGEMENT_REQUIRED");
        await fixture.Authentication.DidNotReceiveWithAnyArgs().CreateCredentialChallengeAsync(
            default, default!, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task PasswordReset_AdminIssued_RejectsExpiryOutsideCanonicalPolicy()
    {
        var fixture = new Fixture();

        var result = await fixture.Gateway.IssueCredentialChallengeAsync(Actor,
            new(Guid.NewGuid(), "PASSWORD_RESET", DateTimeOffset.UtcNow.AddHours(1), "NO_EMAIL_RECOVERY",
                Guid.NewGuid(), ActivationDeliveryModes.AdminIssued, true), CancellationToken.None);

        result.Classification.Should().Be("INVALID_CREDENTIAL_CHALLENGE_EXPIRY");
        await fixture.Authentication.DidNotReceiveWithAnyArgs().CreateCredentialChallengeAsync(
            default, default!, default!, default, default, default, default, default);
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

        public Fixture(bool challengeDeliveryEnabled = false)
        {
            Delivery.Enabled.Returns(challengeDeliveryEnabled);
            Links.Enabled.Returns(true);
            Links.BuildUrl(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>())
                .Returns(call => $"https://accounts.example.test/account/{(call.ArgAt<string>(0) == "PASSWORD_RESET" ? "reset-password" : "activate")}?challengeReference={call.ArgAt<Guid>(1):D}&challengeSecret={call.ArgAt<string>(2)}");
            Authentication.GetCredentialChallengeTargetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => new CredentialChallengeTarget(call.ArgAt<Guid>(0), "ACTIVE", "employee@example.test", 1));
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
}
