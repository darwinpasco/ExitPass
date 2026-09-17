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
    public async Task CreateUser_GeneratesTemporaryPasswordAndTotpForEveryHuman()
    {
        var issuedAt = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var passwords = Substitute.For<IHumanPasswordHasher>();
        var totp = Substitute.For<ITotpProvider>();
        var protector = Substitute.For<ITotpSecretProtector>();
        passwords.HashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new PasswordHashMaterial(new byte[32], new byte[16], "ARGON2ID", 19, 3, 65536, 1));
        totp.GenerateSecret().Returns(new byte[20]);
        totp.EncodeSecret(Arg.Any<byte[]>()).Returns("TOTP-SHARED-SECRET");
        totp.BuildProvisioningUri("operator01", Arg.Any<byte[]>()).Returns("otpauth://totp/ExitPass:operator01");
        protector.IsConfigured.Returns(true);
        protector.KeyReference.Returns("test-key");
        protector.KeyVersion.Returns("1");
        protector.EnvelopeFormatVersion.Returns((short)1);
        protector.Protect(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<byte[]>()).Returns(new byte[48]);
        var command = CreateInvitation(null);
        repository.CreateUserAsync(Actor, Arg.Any<CreateIdentityUserCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var persisted = call.ArgAt<CreateIdentityUserCommand>(1);
                var user = new IdentityUserSummary(persisted.Bootstrap!.UserReference, "operator01", "Operator One",
                    null, null, "SITE_OPERATOR", "ACTIVE", DateTimeOffset.UtcNow, null, null, 1);
                return IdentityAdministrationResult<IdentityUserSummary>.Succeeded(user, command.CorrelationId);
            });
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), new FixedTimeProvider(issuedAt), passwords, totp, protector);

        var result = await service.CreateInvitedUserAsync(Actor, command, CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        result.Value!.Invitation.InvitationState.Should().Be("PASSWORD_CHANGE_REQUIRED");
        result.Value.OneTimeBootstrap!.TemporaryPassword.Should().StartWith("Ep1!");
        result.Value.OneTimeBootstrap.TotpSharedSecret.Should().Be("TOTP-SHARED-SECRET");
        result.Value.OneTimeBootstrap.TemporaryPasswordExpiresAt.Should().Be(issuedAt.AddHours(72));
        result.Value.OneTimeBootstrap.PasswordChangeRequired.Should().BeTrue();
        await repository.Received(1).CreateUserAsync(Actor,
            Arg.Is<CreateIdentityUserCommand>(value => value.Bootstrap != null &&
                value.Bootstrap.TemporaryPasswordExpiresAt == issuedAt.AddHours(72)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUser_FailsClosedWhenTotpProtectionIsUnavailable()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var gateway = Substitute.For<IHumanAuthenticationAdministrationGateway>();
        var passwords = Substitute.For<IHumanPasswordHasher>();
        var totp = Substitute.For<ITotpProvider>();
        var protector = Substitute.For<ITotpSecretProtector>();
        protector.IsConfigured.Returns(false);
        var service = new ManagementPlatformIdentityAdministrationService(repository, gateway,
            Options.Create(new HumanAuthenticationOptions()), TimeProvider.System, passwords, totp, protector);

        var result = await service.CreateInvitedUserAsync(Actor,
            CreateInvitation(null), CancellationToken.None);

        result.Classification.Should().Be("HUMAN_BOOTSTRAP_CRYPTOGRAPHY_UNAVAILABLE");
        await repository.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default);
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
    public async Task ListRoles_ProjectsAuthoritativeApplicationAndScopePolicy()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var service = new ManagementPlatformIdentityAdministrationService(
            repository, Substitute.For<IHumanAuthenticationAdministrationGateway>());
        var correlationId = Guid.NewGuid();
        var role = RoleDefinition(ApprovedIdentityRoleCatalog.OperationsSupervisor);
        repository.ListRolesAsync(Actor, Arg.Any<IdentityRoleCatalogQuery>(), correlationId,
                Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>.Succeeded(
                [role], correlationId));

        var result = await service.ListRolesAsync(Actor, new("SITE_OPERATOR", false), correlationId,
            CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        result.Value.Should().ContainSingle();
        result.Value![0].ApplicationAccess.Should().BeEquivalentTo(
            ApprovedIdentityRoleCatalog.ManagementPlatformAudience,
            ApprovedIdentityRoleCatalog.OperatorConsoleAudience);
        result.Value[0].ScopePolicy.Should().BeEquivalentTo(new IdentityRoleScopePolicy(
            [ApprovedIdentityRoleCatalog.SiteScope], true, ApprovedIdentityRoleCatalog.SiteScope));
        await repository.Received(1).ListRolesAsync(Actor,
            Arg.Is<IdentityRoleCatalogQuery>(query => query.UserType == null), correlationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListRoles_UnknownRoleFailsClosedWithoutPartialCatalog()
    {
        var repository = Substitute.For<IManagementPlatformIdentityAdministrationRepository>();
        var service = new ManagementPlatformIdentityAdministrationService(
            repository, Substitute.For<IHumanAuthenticationAdministrationGateway>());
        var correlationId = Guid.NewGuid();
        repository.ListRolesAsync(Actor, Arg.Any<IdentityRoleCatalogQuery>(), correlationId,
                Arg.Any<CancellationToken>())
            .Returns(IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>.Succeeded(
                [RoleDefinition("UNKNOWN_ROLE")], correlationId));

        var result = await service.ListRolesAsync(Actor, new(null, false), correlationId,
            CancellationToken.None);

        result.Outcome.Should().Be(IdentityAdministrationOutcome.IntegrationUnavailable);
        result.Classification.Should().Be("IDENTITY_ROLE_POLICY_UNAVAILABLE");
        result.Value.Should().BeNull();
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

    private static CreateIdentityUserCommand CreateInvitation(string? email) =>
        new("operator01", "Operator One", email, null, "SITE_OPERATOR", Guid.NewGuid(), "SITE",
            Guid.NewGuid(), null, DateTimeOffset.UtcNow, null, "ONBOARDING", "invite-1", Guid.NewGuid());

    private static IdentityRoleDefinition RoleDefinition(string code) =>
        new(Guid.NewGuid(), code, code, null, "SYSTEM", "ACTIVE", false, false,
            DateTimeOffset.UtcNow.AddDays(-1), null, 1, "CANONICAL_ROLE", true, true, []);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
