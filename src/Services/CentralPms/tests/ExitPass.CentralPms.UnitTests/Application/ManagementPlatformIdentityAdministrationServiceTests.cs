using ExitPass.CentralPms.Application.ManagementPlatform;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformIdentityAdministrationServiceTests
{
    private static readonly IdentityAdministrationActor Actor = new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task CreateUser_NormalizesControlledCodesWithoutAcceptingActorFromRequest()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway);
        var correlationId = Guid.NewGuid();
        var command = new CreateIdentityUserCommand(
            " Cashier01 ", " Cashier One ", null, null, " site_operator ",
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
                value.ReasonCode == "ONBOARDING" &&
                value.IdempotencyKey == "invite-1"),
            Arg.Any<CancellationToken>());
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
}
