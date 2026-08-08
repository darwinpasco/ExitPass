using ExitPass.CentralPms.Contracts.HumanAuthentication;

namespace ExitPass.CentralPms.Application.HumanAuthentication;

public static class HumanSessionAudiences
{
    public const string ManagementPlatform = "MANAGEMENT_PLATFORM";
    public const string OperatorConsole = "OPERATOR_CONSOLE";
    public const string Apt = "APT";

    public static bool IsWeb(string audience) =>
        audience is ManagementPlatform or OperatorConsole;

    public static bool IsKnown(string audience) =>
        IsWeb(audience) || audience == Apt;
}

public static class HumanAuthenticationOutcomes
{
    public const string Authenticated = "AUTHENTICATED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string MfaEnrollmentRequired = "MFA_ENROLLMENT_REQUIRED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountUnavailable = "ACCOUNT_UNAVAILABLE";
    public const string Throttled = "THROTTLED";
    public const string SessionInvalid = "SESSION_INVALID";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string Forbidden = "FORBIDDEN";
}

public sealed record HumanAuthenticationContext(
    Guid CorrelationId,
    Guid CentralPmsServiceIdentityId,
    string? SourceIpHash,
    string? UserAgentHash,
    Guid? DeviceServiceIdentityId,
    Guid? SiteId);

public sealed record LocalCredentialRecord(
    Guid LocalCredentialId,
    string Status,
    byte[] PasswordVerifier,
    byte[] Salt,
    string AlgorithmCode,
    short AlgorithmVersion,
    int Iterations,
    int? MemoryKiB,
    short? Parallelism,
    long CredentialVersion,
    long RowVersion);

public sealed record TotpAuthenticatorRecord(
    Guid AuthenticatorId,
    string Status,
    byte[] ProtectedSecretEnvelope,
    string ProtectionKeyReference,
    string ProtectionKeyVersion,
    short EnvelopeFormatVersion,
    long? LastSuccessfullyUsedTimeStep,
    long RowVersion);

public sealed record HumanLoginRecord(
    Guid UserId,
    string Username,
    string DisplayName,
    string UserStatus,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? LockoutExpiresAt,
    string? LockoutReasonCode,
    long CredentialVersion,
    long AuthorizationEpoch,
    bool HasPrivilegedRole,
    LocalCredentialRecord? Credential,
    TotpAuthenticatorRecord? TotpAuthenticator);

public sealed record HumanSessionRecord(
    Guid HumanSessionId,
    Guid SessionReference,
    string SessionSecretHash,
    Guid UserId,
    string Username,
    string DisplayName,
    string UserStatus,
    DateTimeOffset UserEffectiveFrom,
    DateTimeOffset? UserEffectiveTo,
    DateTimeOffset? LockoutExpiresAt,
    string AuthenticationProvider,
    Guid? LocalCredentialId,
    string? LocalCredentialStatus,
    Guid? ExternalIdentityBindingId,
    string Audience,
    Guid? DeviceServiceIdentityId,
    string SessionStatus,
    string AssuranceContext,
    bool MfaRequirementSatisfied,
    Guid? MfaAuthenticatorId,
    DateTimeOffset? MfaVerifiedAt,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    long CredentialVersionSnapshot,
    long AuthorizationEpochSnapshot,
    long CurrentCredentialVersion,
    long CurrentAuthorizationEpoch,
    bool HasPrivilegedRole,
    Guid CorrelationId,
    long RowVersion);

public sealed record EffectiveHumanAuthorization(
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> SiteIds,
    IReadOnlyList<Guid> SiteGroupIds,
    bool HasGlobalScope);

public sealed record SessionCredential(Guid SessionReference, string Secret, string SerializedToken);

public sealed record SessionIssue(
    Guid HumanSessionId,
    SessionCredential Credential,
    HumanSessionRecord Record);

public sealed record HumanAuthenticationResult(
    int HttpStatusCode,
    HumanAuthenticationResponse Response,
    SessionCredential? Credential = null,
    Guid? InternalHumanSessionId = null);

public sealed record TotpEnrollmentResult(
    int HttpStatusCode,
    TotpEnrollmentResponse Response);

public sealed record PasswordHashMaterial(
    byte[] Verifier,
    byte[] Salt,
    string AlgorithmCode,
    short AlgorithmVersion,
    int Iterations,
    int MemoryKiB,
    short Parallelism);

public sealed record TotpVerificationResult(bool Succeeded, long? MatchedTimeStep);

public sealed record CredentialChallengeDeliveryRequest(
    Guid UserId,
    string Purpose,
    Guid ChallengeReference,
    string ChallengeSecret,
    DateTimeOffset ExpiresAt,
    Guid CorrelationId);
