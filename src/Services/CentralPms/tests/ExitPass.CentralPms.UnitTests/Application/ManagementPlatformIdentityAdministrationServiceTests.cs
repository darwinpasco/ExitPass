using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.HumanAuthentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformIdentityAdministrationServiceTests
{
    private static readonly IdentityAdministrationActor Actor = new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task CreateInvitedUser_EmailRequiresUsableAddressBeforePersistence()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        gateway.ActivationLinkEnabled.Returns(true);
        gateway.EmailDeliveryEnabled.Returns(true);
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), TimeProvider.System);
        var command = CreateInvitation(null, ActivationDeliveryModes.Email, false);

        var result = await service.CreateInvitedUserAsync(Actor, command, CancellationToken.None);

        result.Classification.Should().Be("ACTIVATION_EMAIL_REQUIRED");
        await repository.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateInvitedUser_AdminIssuedAllowsNoEmailAndReturnsOneTimeMaterial()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        gateway.ActivationLinkEnabled.Returns(true);
        var command = CreateInvitation(null, ActivationDeliveryModes.AdminIssued, true);
        var user = new IdentityUserSummary(Guid.NewGuid(), "operator01", "Operator One", null, null,
            "SITE_OPERATOR", "INVITED", DateTimeOffset.UtcNow, null, null, 1);
        repository.CreateUserAsync(Actor, Arg.Any<CreateIdentityUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityUserSummary>.Succeeded(user, command.CorrelationId));
        var challengeReference = Guid.NewGuid();
        var material = new OneTimeActivationMaterial(challengeReference, "one-time-secret",
            DateTimeOffset.UtcNow.AddMinutes(30), "https://accounts.example.test/account/activate?challenge", "qr");
        gateway.IssueCredentialChallengeAsync(Actor, Arg.Any<CreateCredentialResetChallengeCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => IdentityAdministrationResult<CredentialResetChallengeResult>.Succeeded(
                new(challengeReference, call.ArgAt<CreateCredentialResetChallengeCommand>(1).ExpiresAt,
                    ActivationDeliveryModes.AdminIssued, "ADMIN_ISSUED", material), command.CorrelationId));
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), TimeProvider.System);

        var result = await service.CreateInvitedUserAsync(Actor, command, CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        result.Value!.User.Should().Be(user);
        result.Value.OneTimeActivation.Should().Be(material);
        await gateway.Received(1).IssueCredentialChallengeAsync(Actor,
            Arg.Is<CreateCredentialResetChallengeCommand>(value => value.Purpose == "ACCOUNT_ACTIVATION" &&
                value.DeliveryMode == ActivationDeliveryModes.AdminIssued && value.AdminIssuedHandoffAcknowledged),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateInvitedUser_EmailConfigurationUnavailableFailsBeforePersistence()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        gateway.ActivationLinkEnabled.Returns(true);
        gateway.EmailDeliveryEnabled.Returns(false);
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), TimeProvider.System);

        var result = await service.CreateInvitedUserAsync(Actor,
            CreateInvitation("operator@example.test", ActivationDeliveryModes.Email, false), CancellationToken.None);

        result.Classification.Should().Be("CREDENTIAL_CHALLENGE_EMAIL_DELIVERY_NOT_CONFIGURED");
        await repository.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateInvitedUser_EmailDeliveryFailureLeavesPersistedInvitationAvailableForReissue()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        gateway.ActivationLinkEnabled.Returns(true);
        gateway.EmailDeliveryEnabled.Returns(true);
        var command = CreateInvitation("operator@example.test", ActivationDeliveryModes.Email, false);
        var user = new IdentityUserSummary(Guid.NewGuid(), "operator01", "Operator One", "o***@example.test", null,
            "SITE_OPERATOR", "INVITED", DateTimeOffset.UtcNow, null, null, 1);
        repository.CreateUserAsync(Actor, Arg.Any<CreateIdentityUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityUserSummary>.Succeeded(user, command.CorrelationId));
        gateway.IssueCredentialChallengeAsync(Actor, Arg.Any<CreateCredentialResetChallengeCommand>(), Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<CredentialResetChallengeResult>.Failed(
                IdentityAdministrationOutcome.IntegrationUnavailable, "CREDENTIAL_CHALLENGE_DELIVERY_FAILED",
                "Invitation delivery failed.", command.CorrelationId));
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), TimeProvider.System);

        var result = await service.CreateInvitedUserAsync(Actor, command, CancellationToken.None);

        result.Classification.Should().Be("CREDENTIAL_CHALLENGE_DELIVERY_FAILED");
        await repository.Received(1).CreateUserAsync(Actor, Arg.Any<CreateIdentityUserCommand>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceiveWithAnyArgs().CancelInvitationAsync(default!, default!, default);
    }

    [Fact]
    public async Task ReissueInvitation_UsesSameUserAndDoesNotCreateRoleOrScopeAgain()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        gateway.ActivationLinkEnabled.Returns(true);
        var userId = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var user = new IdentityUserSummary(userId, "operator01", "Operator One", null, null,
            "SITE_OPERATOR", "INVITED", DateTimeOffset.UtcNow, null, null, 4);
        repository.GetUserAsync(Actor, userId, correlation, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityUserDetail>.Succeeded(new(user, [], [], null), correlation));
        repository.AuthorizeAuthenticationAdministrationAsync(Actor, userId, "CREDENTIAL_RESET", correlation,
                Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<bool>.Succeeded(true, correlation));
        gateway.IssueCredentialChallengeAsync(Actor, Arg.Any<CreateCredentialResetChallengeCommand>(), Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<CredentialResetChallengeResult>.Succeeded(
                new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(30), ActivationDeliveryModes.AdminIssued,
                    "ADMIN_ISSUED", null), correlation));
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), TimeProvider.System);

        var result = await service.ReissueInvitationAsync(Actor,
            new(userId, ActivationDeliveryModes.AdminIssued, "REISSUE", true, correlation), CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        await repository.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
        await repository.DidNotReceiveWithAnyArgs().AssignRoleAsync(default!, default!, default);
        await repository.DidNotReceiveWithAnyArgs().GrantScopeAsync(default!, default!, default);
    }

    [Fact]
    public async Task DelegableScopes_ForwardsTheAuthenticatedActorAndCorrelation()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var service = new ManagementPlatformIdentityAdministrationService(
            repository, Substitute.For<IHumanAuthenticationAdministrationGateway>());
        var correlationId = Guid.NewGuid();
        var expected = IdentityAdministrationResult<DelegableScopeCatalog>.Succeeded(
            new DelegableScopeCatalog([], []), correlationId);
        repository.GetDelegableScopesAsync(Actor, correlationId, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await service.GetDelegableScopesAsync(Actor, correlationId, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await repository.Received(1).GetDelegableScopesAsync(Actor, correlationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUser_NormalizesControlledCodesWithoutAcceptingActorFromRequest()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway);
        var correlationId = Guid.NewGuid();
        var command = new CreateIdentityUserCommand(
            " Cashier01 ", " Cashier One ", null, null, " site_operator ",
            Guid.NewGuid(), " site ", Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, null, " onboarding ", " invite-1 ", correlationId);
        repository.CreateUserAsync(Actor, Arg.Any<CreateIdentityUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IdentityUserSummary>.Failed(
                IdentityAdministrationOutcome.Conflict, "EXPECTED", "expected", correlationId));

        await service.CreateUserAsync(Actor, command, CancellationToken.None);

        await repository.Received(1).CreateUserAsync(
            Actor,
            Arg.Is<CreateIdentityUserCommand>(value =>
                value.Username == "Cashier01" &&
                value.DisplayName == "Cashier One" &&
                value.UserType == "SITE_OPERATOR" &&
                value.InitialScopeType == "SITE" &&
                value.ReasonCode == "ONBOARDING" &&
                value.IdempotencyKey == "invite-1"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("INTERNAL_ADMIN")]
    [InlineData("OPERATIONS_USER")]
    [InlineData("SITE_OPERATOR")]
    [InlineData("SUPPORT_USER")]
    [InlineData("FINANCE_USER")]
    [InlineData("COMPLIANCE_USER")]
    [InlineData("MERCHANT_USER")]
    [InlineData("SECURITY_USER")]
    [InlineData("OTHER")]
    public async Task CreateUser_AcceptsEveryAuthoritativeUserType(string userType)
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var service = new ManagementPlatformIdentityAdministrationService(
            repository, Substitute.For<IHumanAuthenticationAdministrationGateway>());
        var command = new CreateIdentityUserCommand(
            "authoritative.user", "Authoritative User", null, null, userType.ToLowerInvariant(),
            Guid.NewGuid(), "SITE", Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, null, "TEST", "authoritative-user-type", Guid.NewGuid());

        await service.CreateUserAsync(Actor, command, CancellationToken.None);

        await repository.Received(1).CreateUserAsync(
            Actor,
            Arg.Is<CreateIdentityUserCommand>(value => value.UserType == userType),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUser_RejectsUnsupportedUserTypeBeforePersistence()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var service = new ManagementPlatformIdentityAdministrationService(
            repository, Substitute.For<IHumanAuthenticationAdministrationGateway>());
        var command = new CreateIdentityUserCommand(
            "unsupported.user", "Unsupported User", null, null, "HUMAN",
            Guid.NewGuid(), "SITE", Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, null, "TEST", "unsupported-user-type", Guid.NewGuid());

        var action = () => service.CreateUserAsync(Actor, command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be(nameof(command.UserType));
        await repository.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("SITE", null, null)]
    [InlineData("SITE_GROUP", null, null)]
    [InlineData("GLOBAL", "00000000-0000-0000-0000-000000000001", null)]
    public async Task GrantScope_PreservesShapeForRepositoryFailClosedValidation(string scopeType, string? site, string? siteGroup)
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway);
        var command = new GrantIdentityScopeCommand(
            Guid.NewGuid(), Guid.NewGuid(), scopeType,
            site is null ? null : Guid.Parse(site), siteGroup is null ? null : Guid.Parse(siteGroup),
            DateTimeOffset.UtcNow, null, "TEST", "scope-1", Guid.NewGuid());

        await service.GrantScopeAsync(Actor, command, CancellationToken.None);

        await repository.Received(1).GrantScopeAsync(Actor, Arg.Is<GrantIdentityScopeCommand>(value => value.ScopeType == scopeType), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MfaReset_DelegatesOnlyToI020AdministrationBoundary()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway);
        var command = new ChangeIdentityMfaCommand(Guid.NewGuid(), "reset", 3, "security response", Guid.NewGuid());
        repository.AuthorizeAuthenticationAdministrationAsync(
                Actor, command.UserReference, "MFA_RESET", command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<bool>.Succeeded(true, command.CorrelationId));

        await service.ChangeMfaAsync(Actor, command, CancellationToken.None);

        await gateway.Received(1).ChangeMfaAsync(
            Actor,
            Arg.Is<ChangeIdentityMfaCommand>(value => value.Action == "RESET" && value.ReasonCode == "SECURITY RESPONSE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SessionRevoke_DelegatesOnlyToI020AdministrationBoundary()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway);
        var command = new RevokeIdentitySessionCommand(Guid.NewGuid(), Guid.NewGuid(), "compromise", Guid.NewGuid());
        repository.AuthorizeAuthenticationAdministrationAsync(
                Actor, command.UserReference, "SESSION_REVOKE", command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<bool>.Succeeded(true, command.CorrelationId));

        await service.RevokeSessionsAsync(Actor, command, CancellationToken.None);

        await gateway.Received(1).RevokeSessionsAsync(
            Actor,
            Arg.Is<RevokeIdentitySessionCommand>(value => value.ReasonCode == "COMPROMISE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthenticationAdministration_WhenRepositoryDenies_DoesNotInvokeI020Gateway()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway);
        var command = new ChangeIdentityMfaCommand(Guid.NewGuid(), "remove", 4, "security response", Guid.NewGuid());
        repository.AuthorizeAuthenticationAdministrationAsync(
                Actor, command.UserReference, "MFA_REMOVE", command.CorrelationId, Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<bool>.Failed(
                IdentityAdministrationOutcome.Forbidden,
                "MFA_PRIVILEGE_CEILING_EXCEEDED",
                "The operation is not permitted.",
                command.CorrelationId));

        var result = await service.ChangeMfaAsync(Actor, command, CancellationToken.None);

        result.Classification.Should().Be("MFA_PRIVILEGE_CEILING_EXCEEDED");
        await gateway.DidNotReceiveWithAnyArgs().ChangeMfaAsync(default!, default!, default);
    }

    private static CreateIdentityUserCommand CreateInvitation(string? email, string mode, bool acknowledged) =>
        new("operator01", "Operator One", email, null, "SITE_OPERATOR", Guid.NewGuid(), "SITE",
            Guid.NewGuid(), null, DateTimeOffset.UtcNow, null, "ONBOARDING", "invite-1", Guid.NewGuid(),
            mode, acknowledged);
}
