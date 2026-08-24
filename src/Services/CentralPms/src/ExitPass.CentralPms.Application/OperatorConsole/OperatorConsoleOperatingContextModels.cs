namespace ExitPass.CentralPms.Application.OperatorConsole;

public static class OperatorConsoleOperatingContextFailureCodes
{
    public const string DeviceBindingRequired = "OPERATOR_DEVICE_BINDING_REQUIRED";
    public const string DeviceBindingInvalid = "OPERATOR_DEVICE_BINDING_INVALID";
    public const string DeviceBindingRevoked = "OPERATOR_DEVICE_BINDING_REVOKED";
    public const string DeviceBindingExpired = "OPERATOR_DEVICE_BINDING_EXPIRED";
    public const string DeviceOutsideAuthorizedSite = "OPERATOR_DEVICE_OUTSIDE_AUTHORIZED_SITE";
    public const string ActiveShiftRequired = "OPERATOR_ACTIVE_SHIFT_REQUIRED";
    public const string ActiveShiftConflict = "OPERATOR_ACTIVE_SHIFT_CONFLICT";
    public const string ShiftClosedOrExpired = "OPERATOR_SHIFT_CLOSED_OR_EXPIRED";
    public const string ShiftIncompatibleWithDevice = "OPERATOR_SHIFT_INCOMPATIBLE_WITH_DEVICE";
    public const string ShiftOutsideUserScope = "OPERATOR_SHIFT_OUTSIDE_USER_SCOPE";
    public const string StaleAuthorizationEpoch = "OPERATOR_SESSION_AUTHORIZATION_STALE";
    public const string SessionExpiredOrRevoked = "OPERATOR_SESSION_EXPIRED_OR_REVOKED";
}

public sealed record OperatorConsoleOperatingContext(
    Guid HumanSessionId,
    Guid UserId,
    Guid OperatorDeviceBindingId,
    Guid OperatorShiftId,
    Guid SiteId,
    Guid SiteGroupId,
    long AuthorizationEpoch,
    long CredentialVersion,
    DateTimeOffset BoundAt,
    Guid CorrelationId);

public sealed record OperatorConsoleDeviceBindingCandidate(
    Guid OperatorDeviceBindingId,
    string DeviceStatus,
    string TrustLevel,
    Guid SiteId,
    Guid SiteGroupId,
    bool HasCanonicalSiteGroupRelationship,
    DateTimeOffset? CredentialExpiresAt,
    int MatchingProofCount,
    int ActiveAssignmentCount,
    Guid? AssignmentSiteId,
    Guid? AssignmentSiteGroupId);

public sealed record OperatorConsoleShiftResolution(
    int CompatibleActiveShiftCount,
    Guid? OperatorShiftId,
    bool HasClosedOrExpiredShift,
    bool HasActiveShiftOutsideDevice,
    bool HasActiveShiftOutsideUserScope);

public sealed record OperatorConsoleSessionBindingSnapshot(
    long AuthorizationEpoch,
    long CredentialVersion,
    string SessionStatus,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt);

public sealed record OperatorConsoleOperatingContextValidationFacts(
    OperatorConsoleOperatingContext? Context,
    string? ContextStatus,
    string? CurrentProofThumbprint,
    string? DeviceStatus,
    string? TrustLevel,
    DateTimeOffset? DeviceCredentialExpiresAt,
    int ActiveAssignmentCount,
    Guid? AssignmentSiteId,
    Guid? AssignmentSiteGroupId,
    string? ShiftStatus,
    Guid? ShiftUserId,
    Guid? ShiftSiteId,
    Guid? ShiftSiteGroupId,
    DateTimeOffset? ShiftActiveFrom,
    DateTimeOffset? ShiftActiveTo,
    DateTimeOffset? ShiftRevokedAt,
    string? SessionStatus,
    DateTimeOffset? SessionIdleExpiresAt,
    DateTimeOffset? SessionAbsoluteExpiresAt,
    long CurrentAuthorizationEpoch,
    long CurrentCredentialVersion,
    bool HasEffectiveSiteScope,
    bool HasCanonicalSiteGroupRelationship);

public sealed record OperatorConsoleOperatingContextResult(
    bool Succeeded,
    OperatorConsoleOperatingContext? Context,
    string? ErrorCode,
    Guid CorrelationId)
{
    public static OperatorConsoleOperatingContextResult Success(OperatorConsoleOperatingContext context) =>
        new(true, context, null, context.CorrelationId);

    public static OperatorConsoleOperatingContextResult Failure(string errorCode, Guid correlationId) =>
        new(false, null, errorCode, correlationId);
}

public sealed record OperatorConsoleDeviceCookieIssueResult(
    bool Succeeded,
    string? CookieCredential,
    string? ErrorCode,
    Guid CorrelationId);

public interface IOperatorConsoleOperatingContextRepository
{
    Task<OperatorConsoleDeviceBindingCandidate?> FindDeviceByProofAsync(string proofThumbprint, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RotateDeviceProofAsync(Guid operatorDeviceBindingId, string expectedThumbprint, string replacementThumbprint, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken);
    Task<OperatorConsoleShiftResolution> ResolveShiftAsync(Guid userId, Guid siteId, Guid siteGroupId, IReadOnlyList<Guid> authorizedSiteIds, IReadOnlyList<Guid> authorizedSiteGroupIds, bool hasGlobalScope, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OperatorConsoleSessionBindingSnapshot?> ReadSessionBindingSnapshotAsync(Guid humanSessionId, Guid userId, CancellationToken cancellationToken);
    Task<OperatorConsoleOperatingContext> BindSessionAsync(Guid humanSessionId, Guid userId, Guid operatorDeviceBindingId, Guid operatorShiftId, Guid siteId, Guid siteGroupId, long authorizationEpoch, long credentialVersion, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken);
    Task<OperatorConsoleOperatingContextValidationFacts> ReadValidationFactsAsync(Guid humanSessionId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid humanSessionId, string reasonCode, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken);
    Task TouchAsync(Guid humanSessionId, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken);
}

public interface IOperatorConsoleOperatingContextService
{
    string HashDeviceProof(string proof);
    Task<OperatorConsoleOperatingContextResult> ValidateDeviceProofAsync(string? proof, Guid correlationId, CancellationToken cancellationToken);
    Task<OperatorConsoleDeviceCookieIssueResult> EstablishDeviceBindingAsync(string? provisioningProof, Guid correlationId, CancellationToken cancellationToken);
    Task<OperatorConsoleOperatingContextResult> BindSessionAsync(Guid humanSessionId, Guid userId, IReadOnlyList<Guid> authorizedSiteIds, IReadOnlyList<Guid> authorizedSiteGroupIds, bool hasGlobalScope, string? proof, Guid correlationId, CancellationToken cancellationToken);
    Task<OperatorConsoleOperatingContextResult> ValidateSessionAsync(Guid humanSessionId, string? proof, Guid correlationId, CancellationToken cancellationToken);
}
