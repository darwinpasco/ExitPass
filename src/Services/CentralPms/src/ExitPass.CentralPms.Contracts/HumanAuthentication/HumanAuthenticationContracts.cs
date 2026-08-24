using System.Text.Json.Serialization;

namespace ExitPass.CentralPms.Contracts.HumanAuthentication;

public sealed record HumanLoginRequest(
    string Username,
    string Password,
    string Audience,
    string? TotpCode = null);

public sealed record AptHumanSessionCreateRequest(
    string Username,
    string Password,
    Guid SiteId,
    string? TotpCode = null);

public sealed record HumanSessionContinueRequest(string? SessionToken = null);

public sealed record HumanFreshAuthenticationRequest(string Password, string? TotpCode = null);

public sealed record HumanPasswordChangeRequest(
    string CurrentPassword,
    string NewPassword,
    string? TotpCode = null);

public sealed record HumanPasswordResetRequest(Guid ChallengeReference, string ChallengeSecret, string NewPassword);

public sealed record HumanActivationRequest(Guid ChallengeReference, string ChallengeSecret, string NewPassword);

public sealed record HumanPasswordResetStartRequest(string Username);

public sealed record TotpEnrollmentConfirmRequest(string Code);

public sealed record HumanAuthenticationResponse(
    string Outcome,
    bool Authenticated,
    HumanSessionDto? Session,
    string? AptSessionToken,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId);

public sealed record HumanSessionDto(
    Guid SessionReference,
    Guid UserReference,
    string Username,
    string DisplayName,
    string Audience,
    string Assurance,
    bool PrivilegedAccount,
    bool PasswordChangeRequired,
    bool MfaRequired,
    bool MfaSatisfied,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> SiteReferences,
    IReadOnlyList<Guid> SiteGroupReferences,
    bool HasGlobalScope,
    Guid? DeviceServiceIdentityReference,
    Guid CorrelationId,
    [property: JsonIgnore] Guid? OperatorDeviceBindingReference = null,
    [property: JsonIgnore] Guid? OperatorShiftReference = null,
    [property: JsonIgnore] Guid? EffectiveSiteReference = null,
    [property: JsonIgnore] Guid? EffectiveSiteGroupReference = null,
    [property: JsonIgnore] long? AuthorizationEpoch = null,
    [property: JsonIgnore] long? CredentialVersion = null);

public sealed record TotpEnrollmentResponse(
    string Outcome,
    string? SharedSecret,
    string? ProvisioningUri,
    DateTimeOffset? EnrollmentStartedAt,
    Guid CorrelationId,
    string? ErrorCode = null);

public sealed record HumanChallengeAcceptedResponse(string Outcome, Guid CorrelationId);
