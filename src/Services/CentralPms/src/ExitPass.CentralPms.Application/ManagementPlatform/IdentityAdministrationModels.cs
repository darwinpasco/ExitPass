namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed record IdentityAdministrationActor(Guid UserId, Guid HumanSessionId);

public enum IdentityAdministrationOutcome
{
    Success,
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
    IntegrationUnavailable
}

public sealed record IdentityAdministrationResult<T>(
    IdentityAdministrationOutcome Outcome,
    string Classification,
    string Message,
    Guid CorrelationId,
    T? Value = default)
{
    public static IdentityAdministrationResult<T> Succeeded(T value, Guid correlationId, string classification = "ACCEPTED") =>
        new(IdentityAdministrationOutcome.Success, classification, "The identity administration operation completed.", correlationId, value);

    public static IdentityAdministrationResult<T> Failed(
        IdentityAdministrationOutcome outcome,
        string classification,
        string message,
        Guid correlationId) =>
        new(outcome, classification, message, correlationId);
}

public sealed record IdentityUserSummary(
    Guid UserReference,
    string Username,
    string DisplayName,
    string? MaskedEmail,
    string? MaskedMobileNumber,
    string UserType,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? LastLoginAt,
    long RowVersion);

public sealed record IdentityUserDetail(
    IdentityUserSummary User,
    IReadOnlyList<IdentityRoleAssignment> RoleAssignments,
    IReadOnlyList<IdentityScopeGrant> ScopeGrants);

public sealed record IdentityRoleDefinition(
    Guid RoleReference,
    string Code,
    string Name,
    string? Description,
    string Type,
    string Status,
    bool IsPrivileged,
    bool RequiresElevatedApproval,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    long RowVersion);

public sealed record IdentityPermissionDefinition(
    Guid PermissionReference,
    string Code,
    string Name,
    string Domain,
    string Action,
    string Status,
    bool IsSensitive,
    bool RequiresAudit,
    long RowVersion);

public sealed record IdentityRoleAssignment(
    Guid AssignmentReference,
    Guid UserReference,
    Guid RoleReference,
    string RoleCode,
    string RoleName,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? LastReviewedAt,
    long RowVersion);

public sealed record IdentityScopeGrant(
    Guid GrantReference,
    Guid AssignmentReference,
    string ScopeType,
    Guid? SiteReference,
    Guid? SiteGroupReference,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? LastReviewedAt,
    long RowVersion);

public sealed record IdentityMfaStatus(
    bool RequiredForPrivilegedManagementPlatform,
    bool Enrolled,
    string Status,
    DateTimeOffset? EnrollmentStartedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? LastSuccessfullyUsedAt,
    DateTimeOffset? ResetAt,
    DateTimeOffset? RevokedAt,
    long? RowVersion);

public sealed record IdentitySessionSummary(
    Guid SessionReference,
    string Audience,
    string Status,
    string Assurance,
    bool MfaRequirementSatisfied,
    Guid? DeviceServiceIdentityReference,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt,
    long RowVersion);

public sealed record IdentityPrivilegedAccessRequest(
    Guid RequestReference,
    Guid TargetUserReference,
    Guid RequestedRoleReference,
    string? RequestedScopeType,
    Guid? RequestedSiteReference,
    Guid? RequestedSiteGroupReference,
    string Status,
    string ReasonCode,
    DateTimeOffset RequestedEffectiveFrom,
    DateTimeOffset? RequestedEffectiveTo,
    DateTimeOffset RequestedAt,
    Guid RequestedByUserReference,
    DateTimeOffset? ExpiresAt,
    long RowVersion,
    IReadOnlyList<IdentityPrivilegedAccessDecision> Decisions);

public sealed record IdentityPrivilegedAccessDecision(
    int Sequence,
    string Decision,
    string ReasonCode,
    DateTimeOffset DecidedAt,
    Guid DecidedByUserReference);

public sealed record IdentityAuditEntry(
    Guid AuditReference,
    string EventType,
    string Result,
    string? ReasonCode,
    Guid? ActorUserReference,
    string? Summary,
    DateTimeOffset OccurredAt,
    Guid? CorrelationReference);

public sealed record IdentityUserSearch(int Offset, int Limit, string? Status, string? Query);

public sealed record CreateIdentityUserCommand(
    string Username,
    string DisplayName,
    string? Email,
    string? MaskedMobileNumber,
    string UserType,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ReasonCode,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record UpdateIdentityUserCommand(
    Guid UserReference,
    string DisplayName,
    string? Email,
    string? MaskedMobileNumber,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    long ExpectedRowVersion,
    string ReasonCode,
    Guid CorrelationId);

public sealed record ChangeIdentityUserLifecycleCommand(
    Guid UserReference,
    string Transition,
    DateTimeOffset? LockoutExpiresAt,
    long ExpectedRowVersion,
    string ReasonCode,
    Guid CorrelationId);

public sealed record AssignIdentityRoleCommand(
    Guid UserReference,
    Guid RoleReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ReasonCode,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record RevokeIdentityRoleCommand(
    Guid UserReference,
    Guid AssignmentReference,
    long ExpectedRowVersion,
    string ReasonCode,
    Guid CorrelationId);

public sealed record GrantIdentityScopeCommand(
    Guid UserReference,
    Guid AssignmentReference,
    string ScopeType,
    Guid? SiteReference,
    Guid? SiteGroupReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ReasonCode,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record RevokeIdentityScopeCommand(
    Guid UserReference,
    Guid AssignmentReference,
    Guid GrantReference,
    long ExpectedRowVersion,
    string ReasonCode,
    Guid CorrelationId);

public sealed record CreateCredentialResetChallengeCommand(
    Guid UserReference,
    string Purpose,
    DateTimeOffset ExpiresAt,
    string ReasonCode,
    Guid CorrelationId);

public sealed record CredentialResetChallengeResult(Guid ChallengeReference, DateTimeOffset ExpiresAt);

public sealed record CreatePrivilegedAccessRequestCommand(
    Guid TargetUserReference,
    Guid RoleReference,
    string? ScopeType,
    Guid? SiteReference,
    Guid? SiteGroupReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? ExpiresAt,
    string ReasonCode,
    Guid CorrelationId);

public sealed record DecidePrivilegedAccessCommand(
    Guid RequestReference,
    string Decision,
    string ReasonCode,
    long ExpectedRowVersion,
    Guid CorrelationId);

public sealed record ReviewIdentityAccessCommand(
    Guid UserReference,
    IReadOnlyList<Guid> AssignmentReferences,
    IReadOnlyList<Guid> ScopeGrantReferences,
    string Outcome,
    string ReasonCode,
    Guid CorrelationId);

public sealed record RevokeIdentitySessionCommand(
    Guid UserReference,
    Guid? SessionReference,
    string ReasonCode,
    Guid CorrelationId);

public sealed record ChangeIdentityMfaCommand(
    Guid UserReference,
    string Action,
    long ExpectedRowVersion,
    string ReasonCode,
    Guid CorrelationId);
