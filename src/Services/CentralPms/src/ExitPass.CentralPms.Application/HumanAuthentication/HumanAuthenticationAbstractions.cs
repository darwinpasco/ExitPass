namespace ExitPass.CentralPms.Application.HumanAuthentication;

public interface IHumanAuthenticationRepository
{
    Task<HumanLoginRecord?> FindLocalLoginAsync(string normalizedUsername, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> CountRecentFailedAttemptsAsync(Guid? userId, string loginIdentifierHash, string? sourceIpHash, string attemptType, DateTimeOffset since, CancellationToken cancellationToken);
    Task RecordAuthenticationAttemptAsync(Guid? userId, string loginIdentifierHash, string? sourceIpHash, string? userAgentHash, string attemptType, string result, string audience, string reasonCode, DateTimeOffset observedAt, Guid correlationId, Guid serviceIdentityId, CancellationToken cancellationToken);
    Task ApplyAuthenticationLockoutAsync(Guid userId, DateTimeOffset lockedAt, DateTimeOffset expiresAt, string reasonCode, Guid serviceIdentityId, CancellationToken cancellationToken);
    Task ReleaseExpiredAuthenticationLockoutAsync(Guid userId, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken);
    Task UpdateCredentialVerifierAsync(Guid localCredentialId, long expectedRowVersion, PasswordHashMaterial material, Guid serviceIdentityId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<SessionIssue> CreateSessionAsync(Guid userId, Guid localCredentialId, string audience, Guid? deviceServiceIdentityId, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string assuranceContext, long credentialVersion, long authorizationEpoch, DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt, Guid correlationId, SessionCredential credential, CancellationToken cancellationToken);
    Task<SessionIssue?> RotateSessionAsync(HumanSessionRecord currentSession, SessionCredential replacementCredential, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string assuranceContext, DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt, Guid correlationId, CancellationToken cancellationToken);
    Task<SessionIssue?> ChangePasswordAndRotateSessionAsync(HumanSessionRecord currentSession, long expectedCredentialRowVersion, PasswordHashMaterial material, SessionCredential replacementCredential, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string assuranceContext, DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt, Guid correlationId, CancellationToken cancellationToken);
    Task<HumanSessionRecord?> FindSessionAsync(Guid sessionReference, CancellationToken cancellationToken);
    Task<EffectiveHumanAuthorization> GetEffectiveAuthorizationAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> TouchSessionAsync(Guid humanSessionId, long expectedRowVersion, DateTimeOffset now, DateTimeOffset idleExpiresAt, CancellationToken cancellationToken);
    Task MarkSessionExpiredAsync(Guid humanSessionId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeSessionAsync(Guid humanSessionId, Guid actorUserId, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> RevokeAllUserSessionsAsync(Guid userId, Guid actorUserId, string reasonCode, DateTimeOffset now, Guid? exceptHumanSessionId, CancellationToken cancellationToken);
    Task<bool> IsActiveDeviceServiceAtSiteAsync(Guid serviceIdentityId, Guid siteId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> TryRecordTotpSuccessAsync(Guid authenticatorId, long expectedRowVersion, long matchedTimeStep, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken);
    Task<TotpAuthenticatorRecord?> CreatePendingTotpAuthenticatorAsync(Guid authenticatorId, Guid userId, byte[] protectedEnvelope, string keyReference, string keyVersion, short formatVersion, DateTimeOffset now, Guid actorUserId, CancellationToken cancellationToken);
    Task<TotpAuthenticatorRecord?> GetCurrentTotpAuthenticatorAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> ConfirmTotpAuthenticatorAsync(Guid authenticatorId, long expectedRowVersion, long matchedTimeStep, DateTimeOffset now, Guid actorUserId, CancellationToken cancellationToken);
    Task ResetTotpAuthenticatorAsync(Guid userId, Guid actorUserId, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken);
    Task ChangePasswordAsync(Guid userId, Guid localCredentialId, long expectedCredentialRowVersion, PasswordHashMaterial material, DateTimeOffset now, Guid actorUserId, CancellationToken cancellationToken);
    Task<(Guid Reference, string Secret)> CreateCredentialChallengeAsync(Guid userId, string purpose, DateTimeOffset issuedAt, DateTimeOffset expiresAt, Guid requestorServiceIdentityId, Guid correlationId, CancellationToken cancellationToken);
    Task<(Guid UserId, Guid ChallengeId)?> ConsumeCredentialChallengeAsync(Guid challengeReference, string challengeSecretHash, string purpose, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeCredentialChallengeAsync(Guid challengeReference, Guid serviceIdentityId, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken);
    Task<LocalCredentialRecord?> GetCurrentLocalCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task CompletePasswordResetAsync(Guid userId, Guid challengeId, PasswordHashMaterial material, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken);
    Task<Guid?> CompleteCredentialChallengeAsync(Guid challengeReference, string challengeSecretHash, string purpose, PasswordHashMaterial material, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken);
    Task RecordSecurityEventAsync(string eventType, string result, string reasonCode, Guid? targetEntityId, Guid? actorUserId, string? sourceIpHash, string? userAgentHash, Guid correlationId, Guid serviceIdentityId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IHumanPasswordHasher
{
    Task<bool> VerifyAsync(string password, LocalCredentialRecord? credential, CancellationToken cancellationToken);
    Task<PasswordHashMaterial> HashAsync(string password, CancellationToken cancellationToken);
    bool NeedsUpgrade(LocalCredentialRecord credential);
    void ValidateNewPassword(string password);
}

public interface ITotpProvider
{
    byte[] GenerateSecret();
    string EncodeSecret(byte[] secret);
    string BuildProvisioningUri(string accountName, byte[] secret);
    TotpVerificationResult Verify(byte[] secret, string code, DateTimeOffset now);
}

public interface ITotpSecretProtector
{
    bool IsConfigured { get; }
    string KeyReference { get; }
    string KeyVersion { get; }
    short EnvelopeFormatVersion { get; }
    byte[] Protect(Guid userId, Guid authenticatorId, byte[] secret);
    byte[] Unprotect(Guid userId, Guid authenticatorId, TotpAuthenticatorRecord authenticator);
}

public interface IHumanSessionTokenService
{
    SessionCredential Create();
    bool TryParse(string? token, out SessionCredential credential);
    string HashSecret(string secret);
    string HashPrivacyValue(string value);
}

public interface IHumanAuthenticationService
{
    Task<HumanAuthenticationResult> LoginAsync(string username, string password, string audience, string? totpCode, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> ResolveSessionAsync(string token, string? expectedAudience, Guid? expectedDeviceServiceIdentityId, HumanAuthenticationContext context, bool touch, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> ContinueSessionAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> LogoutAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> LogoutAllAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> FreshAuthenticateAsync(string token, string password, string? totpCode, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> ChangePasswordAsync(string token, string currentPassword, string newPassword, string? totpCode, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<TotpEnrollmentResult> BeginTotpEnrollmentAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<TotpEnrollmentResult> ConfirmTotpEnrollmentAsync(string token, string code, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(string username, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> ResetPasswordAsync(Guid challengeReference, string challengeSecret, string newPassword, HumanAuthenticationContext context, CancellationToken cancellationToken);
    Task<HumanAuthenticationResult> ActivateAsync(Guid challengeReference, string challengeSecret, string newPassword, HumanAuthenticationContext context, CancellationToken cancellationToken);
}

public interface IHumanMfaAdministrationService
{
    Task ResetTotpAsync(Guid targetUserId, Guid actorUserId, string reasonCode, Guid correlationId, CancellationToken cancellationToken);
}

public interface IExternalHumanAuthenticationAdapter
{
    bool Enabled { get; }
}

public interface ICredentialChallengeDelivery
{
    bool Enabled { get; }
    Task DeliverAsync(CredentialChallengeDeliveryRequest request, CancellationToken cancellationToken);
}
