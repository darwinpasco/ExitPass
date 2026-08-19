namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record CreateIdentityUserRequest(
    string Username,
    string DisplayName,
    string? Email,
    string? MaskedMobileNumber,
    string UserType,
    Guid InitialRoleReference,
    string InitialScopeType,
    Guid? InitialSiteReference,
    Guid? InitialSiteGroupReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ReasonCode,
    string IdempotencyKey);

public sealed record UpdateIdentityUserRequest(
    string DisplayName,
    string? Email,
    string? MaskedMobileNumber,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    long ExpectedRowVersion,
    string ReasonCode);

public sealed record IdentityLifecycleRequest(
    long ExpectedRowVersion,
    string ReasonCode,
    DateTimeOffset? LockoutExpiresAt = null);

public sealed record CredentialResetChallengeRequest(
    string Purpose,
    DateTimeOffset ExpiresAt,
    string ReasonCode);

public sealed record AssignIdentityRoleRequest(
    Guid RoleReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ReasonCode,
    string IdempotencyKey);

public sealed record RevokeIdentityRoleRequest(long ExpectedRowVersion, string ReasonCode);

public sealed record GrantIdentityScopeRequest(
    string ScopeType,
    Guid? SiteReference,
    Guid? SiteGroupReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ReasonCode,
    string IdempotencyKey);

public sealed record RevokeIdentityScopeRequest(long ExpectedRowVersion, string ReasonCode);

public sealed record CreatePrivilegedAccessRequest(
    Guid TargetUserReference,
    Guid RoleReference,
    string? ScopeType,
    Guid? SiteReference,
    Guid? SiteGroupReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? ExpiresAt,
    string ReasonCode);

public sealed record DecidePrivilegedAccessRequest(string Decision, string ReasonCode, long ExpectedRowVersion);

public sealed record ReviewIdentityAccessRequest(
    IReadOnlyList<Guid> AssignmentReferences,
    IReadOnlyList<Guid> ScopeGrantReferences,
    string Outcome,
    string ReasonCode);

public sealed record RevokeIdentitySessionRequest(string ReasonCode);

public sealed record ChangeIdentityMfaRequest(long ExpectedRowVersion, string ReasonCode);

public sealed record IdentityAdministrationErrorResponse(
    string Classification,
    string Message,
    Guid CorrelationReference,
    bool Retryable);
