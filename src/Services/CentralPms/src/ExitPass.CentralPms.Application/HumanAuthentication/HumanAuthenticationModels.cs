using ExitPass.CentralPms.Contracts.HumanAuthentication;

namespace ExitPass.CentralPms.Application.HumanAuthentication;

public static class HumanSessionAudiences
{
    public const string ManagementPlatform = "MANAGEMENT_PLATFORM";
    public const string OperatorConsole = "OPERATOR_CONSOLE";
    public const string Apt = "APT";
    public const string NativeParkingApp = "NATIVE_PARKING_APP";

    public static bool IsWeb(string audience) =>
        audience is ManagementPlatform or OperatorConsole or NativeParkingApp;

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
    long RowVersion,
    DateTimeOffset? TemporaryPasswordExpiresAt = null);

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
    TotpAuthenticatorRecord? TotpAuthenticator,
    string? Email = null);

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
    bool HasGlobalScope,
    IReadOnlyList<string>? EffectiveRoleCodes = null,
    IReadOnlyList<EffectiveHumanAuthorizedSite>? AuthorizedSites = null);

public sealed record EffectiveHumanAuthorizedSite(
    Guid SiteId,
    string DisplayName,
    Guid SiteGroupId,
    string SiteGroupDisplayName);

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

public sealed record AdminTotpPersistenceMaterial(
    Guid AuthenticatorId,
    byte[] ProtectedSecretEnvelope,
    string ProtectionKeyReference,
    string ProtectionKeyVersion,
    short EnvelopeFormatVersion);

public sealed record AdminTotpProvisioningMaterial(
    string SharedSecret,
    string ProvisioningUri);

public sealed record CredentialChallengeDeliveryRequest(
    Guid UserId,
    string RecipientEmail,
    string Purpose,
    Guid ChallengeReference,
    string ChallengeSecret,
    DateTimeOffset ExpiresAt,
    Guid CorrelationId);

public sealed record CredentialChallengeTarget(
    Guid UserId,
    string Status,
    string? Email,
    int UsableLocalCredentialCount);

public static class CredentialChallengeCompletionOutcomes
{
    public const string Completed = "COMPLETED";
    public const string Invalid = "INVALID";
    public const string Expired = "EXPIRED";
    public const string Revoked = "REVOKED";
    public const string Consumed = "CONSUMED";
    public const string AccountUnavailable = "ACCOUNT_UNAVAILABLE";
    public const string CredentialConflict = "CREDENTIAL_CONFLICT";
}

public sealed record CredentialChallengeCompletionResult(string Outcome, Guid? UserId)
{
    public bool Succeeded => Outcome == CredentialChallengeCompletionOutcomes.Completed && UserId.HasValue;
}
