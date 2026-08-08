namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementPlatformIdentityAdministrationService : IManagementPlatformIdentityAdministrationService
{
    private readonly IManagementPlatformIdentityAdministrationRepository _repository;
    private readonly IHumanAuthenticationAdministrationGateway _authenticationGateway;

    public ManagementPlatformIdentityAdministrationService(
        IManagementPlatformIdentityAdministrationRepository repository,
        IHumanAuthenticationAdministrationGateway authenticationGateway)
    {
        _repository = repository;
        _authenticationGateway = authenticationGateway;
    }

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityUserSummary>>> ListUsersAsync(
        IdentityAdministrationActor actor, IdentityUserSearch search, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListUsersAsync(actor, search with { Offset = Math.Max(0, search.Offset), Limit = Math.Clamp(search.Limit, 1, 200) }, correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserDetail>> GetUserAsync(
        IdentityAdministrationActor actor, Guid userReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetUserAsync(actor, RequireReference(userReference), correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserSummary>> CreateUserAsync(
        IdentityAdministrationActor actor, CreateIdentityUserCommand command, CancellationToken cancellationToken) =>
        _repository.CreateUserAsync(actor, command with
        {
            Username = RequireText(command.Username, 128, nameof(command.Username)),
            DisplayName = RequireText(command.DisplayName, 128, nameof(command.DisplayName)),
            UserType = RequireCode(command.UserType, nameof(command.UserType)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
            IdempotencyKey = RequireText(command.IdempotencyKey, 128, nameof(command.IdempotencyKey))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserSummary>> UpdateUserAsync(
        IdentityAdministrationActor actor, UpdateIdentityUserCommand command, CancellationToken cancellationToken) =>
        _repository.UpdateUserAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            DisplayName = RequireText(command.DisplayName, 128, nameof(command.DisplayName)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserSummary>> ChangeUserLifecycleAsync(
        IdentityAdministrationActor actor, ChangeIdentityUserLifecycleCommand command, CancellationToken cancellationToken) =>
        _repository.ChangeUserLifecycleAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            Transition = RequireCode(command.Transition, nameof(command.Transition)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>> ListRolesAsync(
        IdentityAdministrationActor actor, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListRolesAsync(actor, correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityPermissionDefinition>>> ListPermissionsAsync(
        IdentityAdministrationActor actor, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListPermissionsAsync(actor, correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityRoleAssignment>> AssignRoleAsync(
        IdentityAdministrationActor actor, AssignIdentityRoleCommand command, CancellationToken cancellationToken) =>
        _repository.AssignRoleAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            RoleReference = RequireReference(command.RoleReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
            IdempotencyKey = RequireText(command.IdempotencyKey, 128, nameof(command.IdempotencyKey))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityRoleAssignment>> RevokeRoleAsync(
        IdentityAdministrationActor actor, RevokeIdentityRoleCommand command, CancellationToken cancellationToken) =>
        _repository.RevokeRoleAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            AssignmentReference = RequireReference(command.AssignmentReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityScopeGrant>> GrantScopeAsync(
        IdentityAdministrationActor actor, GrantIdentityScopeCommand command, CancellationToken cancellationToken) =>
        _repository.GrantScopeAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            AssignmentReference = RequireReference(command.AssignmentReference),
            ScopeType = RequireCode(command.ScopeType, nameof(command.ScopeType)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
            IdempotencyKey = RequireText(command.IdempotencyKey, 128, nameof(command.IdempotencyKey))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityScopeGrant>> RevokeScopeAsync(
        IdentityAdministrationActor actor, RevokeIdentityScopeCommand command, CancellationToken cancellationToken) =>
        _repository.RevokeScopeAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            AssignmentReference = RequireReference(command.AssignmentReference),
            GrantReference = RequireReference(command.GrantReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public async Task<IdentityAdministrationResult<CredentialResetChallengeResult>> IssueCredentialChallengeAsync(
        IdentityAdministrationActor actor, CreateCredentialResetChallengeCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            Purpose = RequireCode(command.Purpose, nameof(command.Purpose)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        };
        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, "CREDENTIAL_RESET", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.IssueCredentialChallengeAsync(actor, normalized, cancellationToken)
            : Propagate<CredentialResetChallengeResult>(authorization);
    }

    public Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> CreatePrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor, CreatePrivilegedAccessRequestCommand command, CancellationToken cancellationToken) =>
        _repository.CreatePrivilegedAccessRequestAsync(actor, command with
        {
            TargetUserReference = RequireReference(command.TargetUserReference),
            RoleReference = RequireReference(command.RoleReference),
            ScopeType = string.IsNullOrWhiteSpace(command.ScopeType) ? null : RequireCode(command.ScopeType, nameof(command.ScopeType)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> GetPrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor, Guid requestReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetPrivilegedAccessRequestAsync(actor, RequireReference(requestReference), correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> DecidePrivilegedAccessAsync(
        IdentityAdministrationActor actor, DecidePrivilegedAccessCommand command, CancellationToken cancellationToken) =>
        _repository.DecidePrivilegedAccessAsync(actor, command with
        {
            RequestReference = RequireReference(command.RequestReference),
            Decision = RequireCode(command.Decision, nameof(command.Decision)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<bool>> ReviewAccessAsync(
        IdentityAdministrationActor actor, ReviewIdentityAccessCommand command, CancellationToken cancellationToken) =>
        _repository.ReviewAccessAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            Outcome = RequireCode(command.Outcome, nameof(command.Outcome)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentitySessionSummary>>> ListSessionsAsync(
        IdentityAdministrationActor actor, Guid userReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListSessionsAsync(actor, RequireReference(userReference), correlationId, cancellationToken);

    public async Task<IdentityAdministrationResult<bool>> RevokeSessionsAsync(
        IdentityAdministrationActor actor, RevokeIdentitySessionCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        };
        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, "SESSION_REVOKE", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.RevokeSessionsAsync(actor, normalized, cancellationToken)
            : Propagate<bool>(authorization);
    }

    public Task<IdentityAdministrationResult<IdentityMfaStatus>> GetMfaStatusAsync(
        IdentityAdministrationActor actor, Guid userReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetMfaStatusAsync(actor, RequireReference(userReference), correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<bool>> AuthorizeAuthenticationAdministrationAsync(
        IdentityAdministrationActor actor, Guid userReference, string action, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, RequireReference(userReference), RequireCode(action, nameof(action)), correlationId, cancellationToken);

    public async Task<IdentityAdministrationResult<IdentityMfaStatus>> ChangeMfaAsync(
        IdentityAdministrationActor actor, ChangeIdentityMfaCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            Action = RequireCode(command.Action, nameof(command.Action)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        };
        if (normalized.Action is not ("RESET" or "REMOVE"))
        {
            throw new ArgumentException("The MFA administration action is invalid.", nameof(command));
        }

        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, $"MFA_{normalized.Action}", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.ChangeMfaAsync(actor, normalized, cancellationToken)
            : Propagate<IdentityMfaStatus>(authorization);
    }

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityAuditEntry>>> ListAuditEventsAsync(
        IdentityAdministrationActor actor, Guid userReference, int limit, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListAuditEventsAsync(actor, RequireReference(userReference), Math.Clamp(limit, 1, 200), correlationId, cancellationToken);

    private static Guid RequireReference(Guid value) =>
        value != Guid.Empty ? value : throw new ArgumentException("A non-empty reference is required.");

    private static string RequireText(string value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{parameterName} is required and must not exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string RequireCode(string value, string parameterName) =>
        RequireText(value, 64, parameterName).ToUpperInvariant();

    private static IdentityAdministrationResult<T> Propagate<T>(IdentityAdministrationResult<bool> result) =>
        IdentityAdministrationResult<T>.Failed(result.Outcome, result.Classification, result.Message, result.CorrelationId);
}
