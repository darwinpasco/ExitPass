using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ManagementPlatformIdentityAdministrationRepositoryIntegrationTests
{
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public ManagementPlatformIdentityAdministrationRepositoryIntegrationTests(StatutoryDiscountCanonicalDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task GovernedAdministration_UsesCanonicalScopesVersionsAndAtomicAudit()
    {
        var seed = await SeedAdministratorAsync();
        var repository = new PostgresManagementPlatformIdentityAdministrationRepository(_database.ConnectionString);
        var correlationId = Guid.NewGuid();
        var username = $"i021.user.{Guid.NewGuid():N}";

        var created = await repository.CreateUserAsync(
            seed.Actor,
            new CreateIdentityUserCommand(
                username, "I-021 User", "i021.user@example.invalid", "***0000",
                "SITE_OPERATOR", DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_TEST_CREATE", "create-user", correlationId),
            CancellationToken.None);

        created.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        created.Value!.Status.Should().Be("INVITED");
        created.Value.MaskedEmail.Should().Be("i***@example.invalid");
        var listed = await repository.ListUsersAsync(
            seed.Actor, new IdentityUserSearch(0, 10, "INVITED", username), Guid.NewGuid(), CancellationToken.None);
        listed.Value.Should().ContainSingle(user => user.UserReference == created.Value.UserReference);

        var staleUpdate = await repository.UpdateUserAsync(
            seed.Actor,
            new UpdateIdentityUserCommand(created.Value.UserReference, "Changed", null, null, DateTimeOffset.UtcNow.AddMinutes(-1), null, 999, "I021_STALE", Guid.NewGuid()),
            CancellationToken.None);
        staleUpdate.Outcome.Should().Be(IdentityAdministrationOutcome.Conflict);

        var assigned = await repository.AssignRoleAsync(
            seed.Actor,
            new AssignIdentityRoleCommand(created.Value.UserReference, seed.DelegableRoleId, DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_ROLE", "role-1", Guid.NewGuid()),
            CancellationToken.None);
        assigned.Outcome.Should().Be(IdentityAdministrationOutcome.Success);

        var granted = await repository.GrantScopeAsync(
            seed.Actor,
            new GrantIdentityScopeCommand(created.Value.UserReference, assigned.Value!.AssignmentReference, "SITE", seed.SiteId, null, DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_SITE", "scope-1", Guid.NewGuid()),
            CancellationToken.None);
        granted.Outcome.Should().Be(IdentityAdministrationOutcome.Success);

        var replay = await repository.GrantScopeAsync(
            seed.Actor,
            new GrantIdentityScopeCommand(created.Value.UserReference, assigned.Value.AssignmentReference, "SITE", seed.SiteId, null, DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_SITE", "scope-1", Guid.NewGuid()),
            CancellationToken.None);
        replay.Classification.Should().Be("IDEMPOTENT_REPLAY");
        replay.Value!.GrantReference.Should().Be(granted.Value!.GrantReference);

        var groupGrant = await repository.GrantScopeAsync(
            seed.Actor,
            new GrantIdentityScopeCommand(created.Value.UserReference, assigned.Value.AssignmentReference, "SITE_GROUP", null, seed.SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_SITE_GROUP", "scope-group-1", Guid.NewGuid()),
            CancellationToken.None);
        groupGrant.Outcome.Should().Be(IdentityAdministrationOutcome.Success);

        var global = await repository.GrantScopeAsync(
            seed.Actor,
            new GrantIdentityScopeCommand(created.Value.UserReference, assigned.Value.AssignmentReference, "GLOBAL", null, null, DateTimeOffset.UtcNow, null, "I021_GLOBAL", "global-1", Guid.NewGuid()),
            CancellationToken.None);
        global.Outcome.Should().Be(IdentityAdministrationOutcome.Forbidden);
        global.Classification.Should().Be("GLOBAL_SCOPE_POLICY_NOT_APPROVED");

        var detail = await repository.GetUserAsync(seed.Actor, created.Value.UserReference, Guid.NewGuid(), CancellationToken.None);
        detail.Value!.RoleAssignments.Should().ContainSingle(item => item.AssignmentReference == assigned.Value.AssignmentReference);
        detail.Value.ScopeGrants.Should().Contain(item => item.GrantReference == granted.Value.GrantReference);
        detail.Value.ScopeGrants.Should().Contain(item => item.GrantReference == groupGrant.Value!.GrantReference);

        var reviewed = await repository.ReviewAccessAsync(
            seed.Actor,
            new ReviewIdentityAccessCommand(
                created.Value.UserReference,
                [assigned.Value.AssignmentReference],
                [granted.Value!.GrantReference, groupGrant.Value!.GrantReference],
                "CONFIRMED",
                "I021_REVIEW",
                Guid.NewGuid()),
            CancellationToken.None);
        reviewed.Value.Should().BeTrue();

        var reviewedDetail = (await repository.GetUserAsync(seed.Actor, created.Value.UserReference, Guid.NewGuid(), CancellationToken.None)).Value!;
        var reviewedGroupGrant = reviewedDetail.ScopeGrants.Single(item => item.GrantReference == groupGrant.Value.GrantReference);
        var revokedGroup = await repository.RevokeScopeAsync(
            seed.Actor,
            new RevokeIdentityScopeCommand(created.Value.UserReference, assigned.Value.AssignmentReference, reviewedGroupGrant.GrantReference, reviewedGroupGrant.RowVersion, "I021_REVOKE_GROUP", Guid.NewGuid()),
            CancellationToken.None);
        revokedGroup.Value!.Status.Should().Be("REVOKED");

        var reviewedAssignment = reviewedDetail.RoleAssignments.Single(item => item.AssignmentReference == assigned.Value.AssignmentReference);
        var revokedRole = await repository.RevokeRoleAsync(
            seed.Actor,
            new RevokeIdentityRoleCommand(created.Value.UserReference, reviewedAssignment.AssignmentReference, reviewedAssignment.RowVersion, "I021_REVOKE_ROLE", Guid.NewGuid()),
            CancellationToken.None);
        revokedRole.Value!.Status.Should().Be("REVOKED");

        (await CountAuditEventsAsync(correlationId)).Should().Be(1);
    }

    [Fact]
    public async Task PrivilegedDecision_RequiresIndependentActorAndDoesNotImplicitlyActivate()
    {
        var requester = await SeedAdministratorAsync();
        var decider = await SeedAdministratorAsync();
        var repository = new PostgresManagementPlatformIdentityAdministrationRepository(_database.ConnectionString);
        var target = await repository.CreateUserAsync(
            requester.Actor,
            new CreateIdentityUserCommand(
                $"i021.priv.{Guid.NewGuid():N}", "I-021 Privileged Target", null, null, "INTERNAL_ADMIN",
                DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_PRIV_TARGET", "priv-target", Guid.NewGuid()),
            CancellationToken.None);

        var requested = await repository.CreatePrivilegedAccessRequestAsync(
            requester.Actor,
            new CreatePrivilegedAccessRequestCommand(
                target.Value!.UserReference, requester.SystemAdministratorRoleId, "GLOBAL", null, null,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, DateTimeOffset.UtcNow.AddHours(1), "I021_PRIV_REQUEST", Guid.NewGuid()),
            CancellationToken.None);
        requested.Value!.Status.Should().Be("PENDING_DECISION");

        var self = await repository.DecidePrivilegedAccessAsync(
            requester.Actor,
            new DecidePrivilegedAccessCommand(requested.Value.RequestReference, "APPROVE", "I021_SELF", requested.Value.RowVersion, Guid.NewGuid()),
            CancellationToken.None);
        self.Classification.Should().Be("SELF_PRIVILEGED_APPROVAL_PROHIBITED");

        var approved = await repository.DecidePrivilegedAccessAsync(
            decider.Actor,
            new DecidePrivilegedAccessCommand(requested.Value.RequestReference, "APPROVE", "I021_APPROVE", requested.Value.RowVersion, Guid.NewGuid()),
            CancellationToken.None);
        approved.Value!.Status.Should().Be("APPROVED");
        approved.Value.Decisions.Should().ContainSingle(item => item.DecidedByUserReference == decider.Actor.UserId);
        (await CountActiveRoleAssignmentsAsync(target.Value.UserReference, requester.SystemAdministratorRoleId)).Should().Be(0);
    }

    [Fact]
    public async Task LifecycleAndAuthenticationAdministration_EnforceTransitionsScopesAndSafeMetadata()
    {
        var seed = await SeedAdministratorAsync();
        var repository = new PostgresManagementPlatformIdentityAdministrationRepository(_database.ConnectionString);
        var created = await repository.CreateUserAsync(
            seed.Actor,
            new CreateIdentityUserCommand(
                $"i021.lifecycle.{Guid.NewGuid():N}", "I-021 Lifecycle User", null, null, "SITE_OPERATOR",
                DateTimeOffset.UtcNow.AddMinutes(-1), null, "I021_LIFECYCLE", "lifecycle-user", Guid.NewGuid()),
            CancellationToken.None);

        var activated = await repository.ChangeUserLifecycleAsync(
            seed.Actor,
            new ChangeIdentityUserLifecycleCommand(created.Value!.UserReference, "ACTIVATE", null, created.Value.RowVersion, "I021_ACTIVATE", Guid.NewGuid()),
            CancellationToken.None);
        activated.Value!.Status.Should().Be("ACTIVE");

        var locked = await repository.ChangeUserLifecycleAsync(
            seed.Actor,
            new ChangeIdentityUserLifecycleCommand(created.Value.UserReference, "LOCK", DateTimeOffset.UtcNow.AddMinutes(10), activated.Value.RowVersion, "I021_LOCK", Guid.NewGuid()),
            CancellationToken.None);
        locked.Value!.Status.Should().Be("LOCKED");

        var invalidActivation = await repository.ChangeUserLifecycleAsync(
            seed.Actor,
            new ChangeIdentityUserLifecycleCommand(created.Value.UserReference, "ACTIVATE", null, locked.Value.RowVersion, "I021_INVALID", Guid.NewGuid()),
            CancellationToken.None);
        invalidActivation.Outcome.Should().Be(IdentityAdministrationOutcome.Conflict);

        var unlocked = await repository.ChangeUserLifecycleAsync(
            seed.Actor,
            new ChangeIdentityUserLifecycleCommand(created.Value.UserReference, "UNLOCK", null, locked.Value.RowVersion, "I021_UNLOCK", Guid.NewGuid()),
            CancellationToken.None);
        unlocked.Value!.Status.Should().Be("ACTIVE");

        var selfUnlock = await repository.ChangeUserLifecycleAsync(
            seed.Actor,
            new ChangeIdentityUserLifecycleCommand(seed.Actor.UserId, "UNLOCK", null, 1, "I021_SELF", Guid.NewGuid()),
            CancellationToken.None);
        selfUnlock.Classification.Should().Be("SELF_LIFECYCLE_CHANGE_PROHIBITED");

        (await repository.AuthorizeAuthenticationAdministrationAsync(
            seed.Actor, created.Value.UserReference, "CREDENTIAL_RESET", Guid.NewGuid(), CancellationToken.None)).Outcome
            .Should().Be(IdentityAdministrationOutcome.Success);
        (await repository.AuthorizeAuthenticationAdministrationAsync(
            seed.Actor, created.Value.UserReference, "SESSION_REVOKE", Guid.NewGuid(), CancellationToken.None)).Outcome
            .Should().Be(IdentityAdministrationOutcome.Success);
        (await repository.AuthorizeAuthenticationAdministrationAsync(
            seed.Actor, seed.Actor.UserId, "MFA_REMOVE", Guid.NewGuid(), CancellationToken.None)).Classification
            .Should().Be("SELF_AUTHENTICATION_ADMINISTRATION_PROHIBITED");

        var sessions = await repository.ListSessionsAsync(seed.Actor, created.Value.UserReference, Guid.NewGuid(), CancellationToken.None);
        sessions.Value.Should().BeEmpty();
        var mfa = await repository.GetMfaStatusAsync(seed.Actor, created.Value.UserReference, Guid.NewGuid(), CancellationToken.None);
        mfa.Value!.Status.Should().Be("NOT_ENROLLED");
        mfa.Value.Enrolled.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentRemovalAttempts_PreserveOneActiveIdentityAdministrator()
    {
        var first = await SeedAdministratorAsync();
        var second = await SeedAdministratorAsync();
        var manager = await SeedAdministratorAsync();
        await ConvertToNonSystemUserManagerAsync(manager);
        await RetainOnlyAdministratorsAsync(first.Actor.UserId, second.Actor.UserId);
        var repository = new PostgresManagementPlatformIdentityAdministrationRepository(_database.ConnectionString);

        var outcomes = await Task.WhenAll(
            repository.ChangeUserLifecycleAsync(
                manager.Actor,
                new ChangeIdentityUserLifecycleCommand(first.Actor.UserId, "SUSPEND", null, 1, "I021_CONCURRENT", Guid.NewGuid()),
                CancellationToken.None),
            repository.ChangeUserLifecycleAsync(
                manager.Actor,
                new ChangeIdentityUserLifecycleCommand(second.Actor.UserId, "SUSPEND", null, 1, "I021_CONCURRENT", Guid.NewGuid()),
                CancellationToken.None));

        outcomes.Should().ContainSingle(result => result.Outcome == IdentityAdministrationOutcome.Success);
        outcomes.Should().ContainSingle(result => result.Classification == "LAST_ACTIVE_ADMIN_PROTECTED");
        (await CountActiveSystemAdministratorsAsync()).Should().Be(1);

        var activePrivilegedUserId = outcomes[0].Outcome == IdentityAdministrationOutcome.Success
            ? second.Actor.UserId
            : first.Actor.UserId;
        var ceiling = await repository.AuthorizeAuthenticationAdministrationAsync(
            manager.Actor, activePrivilegedUserId,
            "MFA_REMOVE", Guid.NewGuid(), CancellationToken.None);
        ceiling.Classification.Should().Be("MFA_PRIVILEGE_CEILING_EXCEEDED");
    }

    [Fact]
    public async Task CombinedAuthorization_EnforcesFreshnessPrivilegedTotpAndAuthorizationEpoch()
    {
        var seed = await SeedAdministratorAsync();
        var repository = new PostgresManagementPlatformIdentityAdministrationRepository(_database.ConnectionString, 5);

        (await repository.ListUsersAsync(seed.Actor, new(0, 1, null, null), Guid.NewGuid(), CancellationToken.None))
            .Outcome.Should().Be(IdentityAdministrationOutcome.Success);

        await UpdateSessionAssuranceAsync(seed.Actor.HumanSessionId, "PASSWORD", false, authenticatedMinutesAgo: 1);
        (await repository.ListUsersAsync(seed.Actor, new(0, 1, null, null), Guid.NewGuid(), CancellationToken.None))
            .Outcome.Should().Be(IdentityAdministrationOutcome.Forbidden);

        await ConvertToNonSystemUserManagerAsync(seed);
        (await repository.ListUsersAsync(seed.Actor, new(0, 1, null, null), Guid.NewGuid(), CancellationToken.None))
            .Outcome.Should().Be(IdentityAdministrationOutcome.Success);

        await UpdateSessionAssuranceAsync(seed.Actor.HumanSessionId, "PASSWORD", false, authenticatedMinutesAgo: 10);
        (await repository.ListUsersAsync(seed.Actor, new(0, 1, null, null), Guid.NewGuid(), CancellationToken.None))
            .Outcome.Should().Be(IdentityAdministrationOutcome.Forbidden);

        await UpdateSessionAssuranceAsync(seed.Actor.HumanSessionId, "PASSWORD", false, authenticatedMinutesAgo: 1);
        await IncrementAuthorizationEpochAsync(seed.Actor.UserId);
        (await repository.ListUsersAsync(seed.Actor, new(0, 1, null, null), Guid.NewGuid(), CancellationToken.None))
            .Outcome.Should().Be(IdentityAdministrationOutcome.Forbidden);
    }

    [Fact]
    public async Task CombinedServices_UseRealChallengeMfaAndSessionRevocationRuntime()
    {
        var actor = await SeedAdministratorAsync();
        var target = await SeedAdministratorAsync();
        var options = Options.Create(new HumanAuthenticationOptions
        {
            TotpProtectionKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            TotpProtectionKeyReference = "i021-disposable",
            TotpProtectionKeyVersion = "1"
        });
        var tokens = new HumanSessionTokenService();
        var authenticationRepository = new PostgresHumanAuthenticationRepository(_database.ConnectionString, tokens);
        var delivery = new CapturingCredentialChallengeDelivery();
        var authentication = new HumanAuthenticationService(
            authenticationRepository,
            new Argon2idHumanPasswordHasher(options),
            new TotpProvider(options),
            new AesGcmTotpSecretProtector(options),
            tokens,
            delivery,
            TimeProvider.System,
            options);
        var administrationRepository = new PostgresManagementPlatformIdentityAdministrationRepository(_database.ConnectionString);
        var gateway = new HumanAuthenticationAdministrationGateway(
            authenticationRepository, authentication, delivery, administrationRepository, options, TimeProvider.System);
        var service = new ManagementPlatformIdentityAdministrationService(administrationRepository, gateway);

        var challengeCorrelation = Guid.NewGuid();
        var challenge = await service.IssueCredentialChallengeAsync(actor.Actor,
            new(target.Actor.UserId, "PASSWORD_RESET", DateTimeOffset.UtcNow.AddMinutes(10),
                "I021_ADMIN_RESET", challengeCorrelation), CancellationToken.None);
        challenge.Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        delivery.DeliveredReference.Should().Be(challenge.Value!.ChallengeReference);

        var revokeCorrelation = Guid.NewGuid();
        (await service.RevokeSessionsAsync(actor.Actor,
            new(target.Actor.UserId, target.PublicSessionReference, "I021_ADMIN_REVOKE", revokeCorrelation),
            CancellationToken.None)).Outcome.Should().Be(IdentityAdministrationOutcome.Success);
        (await GetSessionStatusAsync(target.PublicSessionReference)).Should().Be("REVOKED");

        var reset = await service.ChangeMfaAsync(actor.Actor,
            new(target.Actor.UserId, "RESET", target.MfaRowVersion, "I021_MFA_RESET", Guid.NewGuid()),
            CancellationToken.None);
        reset.Value!.Status.Should().Be("RESET_REQUIRED");

        var remove = await service.ChangeMfaAsync(actor.Actor,
            new(target.Actor.UserId, "REMOVE", reset.Value.RowVersion!.Value, "I021_MFA_REMOVE", Guid.NewGuid()),
            CancellationToken.None);
        remove.Value!.Status.Should().Be("REVOKED");
        remove.Value.Enrolled.Should().BeFalse();

        (await CountSecurityEventsAsync("SESSION_REVOKED", revokeCorrelation)).Should().Be(1);
    }

    private async Task<SeedContext> SeedAdministratorAsync()
    {
        var actorUserId = Guid.NewGuid();
        var localCredentialId = Guid.NewGuid();
        var mfaAuthenticatorId = Guid.NewGuid();
        var userRoleId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var sessionReference = Guid.NewGuid();
        var siteGroupId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        const string sql = """
            INSERT INTO sites.site_groups (
                site_group_id, site_group_code, site_group_name, timezone_name, default_currency_code,
                site_group_status, effective_from)
            VALUES (@site_group_id, @site_group_code, 'I-021 Group', 'Asia/Manila', 'PHP', 'ACTIVE', now() - interval '1 day');

            INSERT INTO sites.sites (
                site_id, site_group_id, site_code, site_name, site_type, timezone_name, country_code,
                site_status, effective_from)
            VALUES (@site_id, @site_group_id, @site_code, 'I-021 Site', 'OTHER', 'Asia/Manila', 'PH', 'ACTIVE', now() - interval '1 day');

            INSERT INTO identity.users (
                user_id, username, display_name, user_type, user_status, effective_from)
            VALUES (@actor_user_id, @username, 'I-021 Administrator', 'INTERNAL_ADMIN', 'ACTIVE', now() - interval '1 day');

            INSERT INTO identity.local_credentials (
                local_credential_id, user_id, credential_status, password_verifier, verifier_salt,
                verifier_algorithm_code, verifier_algorithm_version, verifier_work_factor,
                verifier_memory_kib, verifier_parallelism, activated_at, last_changed_at,
                created_by_user_id, updated_by_user_id)
            VALUES (@credential_id, @actor_user_id, 'ACTIVE', decode(repeat('ab', 32), 'hex'), decode(repeat('cd', 16), 'hex'),
                'ARGON2ID', 1, 3, 65536, 1, now(), now(),
                @actor_user_id, @actor_user_id);

            INSERT INTO identity.user_mfa_authenticators (
                user_mfa_authenticator_id, user_id, authenticator_type, authenticator_status,
                protected_secret_envelope, protection_key_reference, protection_key_version,
                envelope_format_version, enrollment_started_at, activated_at,
                created_by_user_id, updated_by_user_id)
            VALUES (@mfa_authenticator_id, @actor_user_id, 'TOTP', 'ACTIVE',
                decode(repeat('ef', 48), 'hex'), 'i021-disposable-envelope', '1', 1,
                now() - interval '1 day', now() - interval '1 minute',
                @actor_user_id, @actor_user_id);

            INSERT INTO identity.user_roles (
                user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT @user_role_id, @actor_user_id, role_id, 'ACTIVE', 'I021_TEST',
                   @actor_user_id, now() - interval '1 day', @actor_user_id, @actor_user_id
            FROM identity.roles WHERE role_code = 'SYSTEM_RBAC_ADMINISTRATOR';

            INSERT INTO identity.user_role_scope_grants (
                user_role_scope_grant_id, user_role_id, scope_type, grant_status, grant_reason_code,
                effective_from, granted_by_user_id, created_by_user_id, updated_by_user_id)
            VALUES (gen_random_uuid(), @user_role_id, 'GLOBAL', 'ACTIVE', 'I021_TEST',
                now() - interval '1 day', @actor_user_id, @actor_user_id, @actor_user_id);

            INSERT INTO identity.human_sessions (
                human_session_id, session_reference, session_secret_hash, user_id, authentication_provider,
                local_credential_id, session_audience, session_status, assurance_context_code,
                mfa_requirement_satisfied, mfa_authenticator_id, mfa_verified_at,
                authenticated_at, last_seen_at, idle_expires_at, absolute_expires_at,
                credential_version_snapshot, authorization_epoch_snapshot, correlation_id)
            VALUES (@session_id, @session_reference, @session_hash, @actor_user_id, 'LOCAL', @credential_id,
                'MANAGEMENT_PLATFORM', 'ACTIVE', 'PASSWORD_TOTP', true,
                @mfa_authenticator_id, now() - interval '1 minute',
                now() - interval '1 minute', now() - interval '1 minute', now() + interval '1 hour', now() + interval '8 hours',
                1, 1, gen_random_uuid());

            INSERT INTO identity.roles (
                role_id, role_code, role_name, role_type, role_status, is_privileged,
                requires_elevated_approval, effective_from)
            VALUES (@role_id, @role_code, 'I-021 Delegable Role', 'OTHER', 'ACTIVE', false, false, now() - interval '1 day');

            INSERT INTO identity.role_permissions (
                role_permission_id, role_id, permission_id, binding_status, binding_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT gen_random_uuid(), r.role_id, p.permission_id, 'ACTIVE', 'I021_TEST',
                   @actor_user_id, now() - interval '1 day', @actor_user_id, @actor_user_id
            FROM identity.roles r
            CROSS JOIN identity.permissions p
            WHERE r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR'
              AND p.permission_code IN (
                  'identity.role-assignment.manage', 'identity.scope-assignment.manage',
                  'identity.privileged-access.decide', 'identity.access-review.manage',
                  'human-authentication.session.admin.view', 'human-authentication.session.admin.revoke',
                  'human-authentication.credential.reset', 'human-authentication.mfa.status.view',
                  'human-authentication.mfa.reset', 'human-authentication.mfa.remove')
              AND NOT EXISTS (
                  SELECT 1 FROM identity.role_permissions existing
                  WHERE existing.role_id = r.role_id AND existing.permission_id = p.permission_id
                    AND existing.binding_status = 'ACTIVE');
            """;

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("site_group_code", $"I021-G-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("site_code", $"I021-S-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("username", $"i021.admin.{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("credential_id", localCredentialId);
        command.Parameters.AddWithValue("mfa_authenticator_id", mfaAuthenticatorId);
        command.Parameters.AddWithValue("user_role_id", userRoleId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("session_reference", sessionReference);
        command.Parameters.AddWithValue("session_hash", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant());
        command.Parameters.AddWithValue("role_id", roleId);
        command.Parameters.AddWithValue("role_code", $"I021_ROLE_{Guid.NewGuid():N}"[..32]);
        await command.ExecuteNonQueryAsync();

        var systemRoleId = await new NpgsqlCommand("SELECT role_id FROM identity.roles WHERE role_code = 'SYSTEM_RBAC_ADMINISTRATOR';", connection).ExecuteScalarAsync();
        return new(new(actorUserId, sessionId), sessionReference, 1, (Guid)systemRoleId!, roleId, siteId, siteGroupId);
    }

    private async Task<long> CountAuditEventsAsync(Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit.audit_events WHERE correlation_id = @correlation_id;", connection);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> CountSecurityEventsAsync(string eventType, Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.security_events WHERE security_event_type = @event_type AND correlation_id = @correlation_id;",
            connection);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> GetSessionStatusAsync(Guid sessionReference)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT session_status::text FROM identity.human_sessions WHERE session_reference = @reference;", connection);
        command.Parameters.AddWithValue("reference", sessionReference);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task ConvertToNonSystemUserManagerAsync(SeedContext manager)
    {
        const string sql = """
            UPDATE identity.user_roles
            SET role_id = @role_id, updated_at = now(), updated_by_user_id = @user_id
            WHERE user_id = @user_id AND assignment_status = 'ACTIVE';

            INSERT INTO identity.role_permissions (
                role_permission_id, role_id, permission_id, binding_status, binding_reason_code,
                assigned_by_user_id, effective_from, created_by_user_id, updated_by_user_id)
            SELECT gen_random_uuid(), @role_id, p.permission_id, 'ACTIVE', 'I021_TEST',
                   @user_id, now() - interval '1 day', @user_id, @user_id
            FROM identity.permissions p
            WHERE p.permission_code IN ('user.view', 'user.manage', 'human-authentication.mfa.remove')
            ON CONFLICT DO NOTHING;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("role_id", manager.DelegableRoleId);
        command.Parameters.AddWithValue("user_id", manager.Actor.UserId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task UpdateSessionAssuranceAsync(
        Guid humanSessionId,
        string assurance,
        bool mfaSatisfied,
        int authenticatedMinutesAgo)
    {
        const string sql = """
            UPDATE identity.human_sessions
            SET assurance_context_code = @assurance,
                mfa_requirement_satisfied = @mfa_satisfied,
                mfa_authenticator_id = CASE WHEN @mfa_satisfied THEN mfa_authenticator_id ELSE NULL END,
                mfa_verified_at = CASE WHEN @mfa_satisfied THEN now() - make_interval(mins => @minutes) ELSE NULL END,
                authenticated_at = now() - make_interval(mins => @minutes),
                last_seen_at = now(),
                updated_at = now(),
                row_version = row_version + 1
            WHERE human_session_id = @session_id;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("session_id", humanSessionId);
        command.Parameters.AddWithValue("assurance", assurance);
        command.Parameters.AddWithValue("mfa_satisfied", mfaSatisfied);
        command.Parameters.AddWithValue("minutes", authenticatedMinutesAgo);
        await command.ExecuteNonQueryAsync();
    }

    private async Task IncrementAuthorizationEpochAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE identity.users SET authorization_epoch = authorization_epoch + 1, row_version = row_version + 1 WHERE user_id = @user_id;",
            connection);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RetainOnlyAdministratorsAsync(Guid firstUserId, Guid secondUserId)
    {
        const string sql = """
            UPDATE identity.users u
            SET user_status = 'INACTIVE', updated_at = now(), row_version = row_version + 1
            WHERE u.user_id NOT IN (@first_user_id, @second_user_id)
              AND EXISTS (
                  SELECT 1 FROM identity.user_roles ur
                  JOIN identity.roles r ON r.role_id = ur.role_id
                  WHERE ur.user_id = u.user_id AND ur.assignment_status = 'ACTIVE'
                    AND r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR');
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("first_user_id", firstUserId);
        command.Parameters.AddWithValue("second_user_id", secondUserId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountActiveSystemAdministratorsAsync()
    {
        const string sql = """
            SELECT count(DISTINCT u.user_id)
            FROM identity.users u
            JOIN identity.user_roles ur ON ur.user_id = u.user_id
            JOIN identity.roles r ON r.role_id = ur.role_id
            WHERE u.user_status = 'ACTIVE' AND ur.assignment_status = 'ACTIVE'
              AND r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR';
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        return (long)(await new NpgsqlCommand(sql, connection).ExecuteScalarAsync())!;
    }

    private async Task<long> CountActiveRoleAssignmentsAsync(Guid userId, Guid roleId)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM identity.user_roles WHERE user_id = @user_id AND role_id = @role_id AND assignment_status = 'ACTIVE';", connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("role_id", roleId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed record SeedContext(
        IdentityAdministrationActor Actor,
        Guid PublicSessionReference,
        long MfaRowVersion,
        Guid SystemAdministratorRoleId,
        Guid DelegableRoleId,
        Guid SiteId,
        Guid SiteGroupId);

    private sealed class CapturingCredentialChallengeDelivery : ICredentialChallengeDelivery
    {
        public bool Enabled => true;
        public Guid? DeliveredReference { get; private set; }

        public Task DeliverAsync(CredentialChallengeDeliveryRequest request, CancellationToken cancellationToken)
        {
            DeliveredReference = request.ChallengeReference;
            return Task.CompletedTask;
        }
    }
}
