namespace ExitPass.CentralPms.Application.ManagementPlatform;

public interface IIdentityAdministrationActorAccessor
{
    IdentityAdministrationActor? Current { get; }
}

public interface IHumanAuthenticationAdministrationGateway
{
    Task<IdentityAdministrationResult<CredentialResetChallengeResult>> IssueCredentialChallengeAsync(
        IdentityAdministrationActor actor,
        CreateCredentialResetChallengeCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<bool>> RevokeSessionsAsync(
        IdentityAdministrationActor actor,
        RevokeIdentitySessionCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityMfaStatus>> ChangeMfaAsync(
        IdentityAdministrationActor actor,
        ChangeIdentityMfaCommand command,
        CancellationToken cancellationToken);
}

public interface IManagementPlatformIdentityAdministrationRepository
{
    Task<IdentityAdministrationResult<IReadOnlyList<IdentityUserSummary>>> ListUsersAsync(
        IdentityAdministrationActor actor,
        IdentityUserSearch search,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityUserDetail>> GetUserAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityUserSummary>> CreateUserAsync(
        IdentityAdministrationActor actor,
        CreateIdentityUserCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityUserSummary>> UpdateUserAsync(
        IdentityAdministrationActor actor,
        UpdateIdentityUserCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityUserSummary>> ChangeUserLifecycleAsync(
        IdentityAdministrationActor actor,
        ChangeIdentityUserLifecycleCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>> ListRolesAsync(
        IdentityAdministrationActor actor,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IReadOnlyList<IdentityPermissionDefinition>>> ListPermissionsAsync(
        IdentityAdministrationActor actor,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityRoleAssignment>> AssignRoleAsync(
        IdentityAdministrationActor actor,
        AssignIdentityRoleCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityRoleAssignment>> RevokeRoleAsync(
        IdentityAdministrationActor actor,
        RevokeIdentityRoleCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityScopeGrant>> GrantScopeAsync(
        IdentityAdministrationActor actor,
        GrantIdentityScopeCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityScopeGrant>> RevokeScopeAsync(
        IdentityAdministrationActor actor,
        RevokeIdentityScopeCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> CreatePrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor,
        CreatePrivilegedAccessRequestCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> GetPrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor,
        Guid requestReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> DecidePrivilegedAccessAsync(
        IdentityAdministrationActor actor,
        DecidePrivilegedAccessCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<bool>> ReviewAccessAsync(
        IdentityAdministrationActor actor,
        ReviewIdentityAccessCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IReadOnlyList<IdentitySessionSummary>>> ListSessionsAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityMfaStatus>> GetMfaStatusAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<bool>> AuthorizeAuthenticationAdministrationAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        string action,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IReadOnlyList<IdentityAuditEntry>>> ListAuditEventsAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        int limit,
        Guid correlationId,
        CancellationToken cancellationToken);
}

public interface IManagementPlatformIdentityAdministrationService : IManagementPlatformIdentityAdministrationRepository
{
    Task<IdentityAdministrationResult<CredentialResetChallengeResult>> IssueCredentialChallengeAsync(
        IdentityAdministrationActor actor,
        CreateCredentialResetChallengeCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<bool>> RevokeSessionsAsync(
        IdentityAdministrationActor actor,
        RevokeIdentitySessionCommand command,
        CancellationToken cancellationToken);

    Task<IdentityAdministrationResult<IdentityMfaStatus>> ChangeMfaAsync(
        IdentityAdministrationActor actor,
        ChangeIdentityMfaCommand command,
        CancellationToken cancellationToken);
}
