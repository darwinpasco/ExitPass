using System.Security.Cryptography;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.HumanAuthentication;

public sealed class HumanAuthenticationService : IHumanAuthenticationService, IHumanMfaAdministrationService
{
    private readonly IHumanAuthenticationRepository _repository;
    private readonly IHumanPasswordHasher _passwords;
    private readonly ITotpProvider _totp;
    private readonly ITotpSecretProtector _totpProtector;
    private readonly IHumanSessionTokenService _tokens;
    private readonly ICredentialChallengeDelivery _challengeDelivery;
    private readonly TimeProvider _timeProvider;
    private readonly HumanAuthenticationOptions _options;

    public HumanAuthenticationService(
        IHumanAuthenticationRepository repository,
        IHumanPasswordHasher passwords,
        ITotpProvider totp,
        ITotpSecretProtector totpProtector,
        IHumanSessionTokenService tokens,
        ICredentialChallengeDelivery challengeDelivery,
        TimeProvider timeProvider,
        IOptions<HumanAuthenticationOptions> options)
    {
        _repository = repository;
        _passwords = passwords;
        _totp = totp;
        _totpProtector = totpProtector;
        _tokens = tokens;
        _challengeDelivery = challengeDelivery;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<HumanAuthenticationResult> LoginAsync(string username, string password, string audience, string? totpCode, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        audience = NormalizeAudience(audience);
        if (!HumanSessionAudiences.IsKnown(audience) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return Failure(400, HumanAuthenticationOutcomes.InvalidCredentials, "INVALID_REQUEST", context.CorrelationId);
        }

        if (audience == HumanSessionAudiences.Apt)
        {
            if (!context.DeviceServiceIdentityId.HasValue || !context.SiteId.HasValue ||
                !await _repository.IsActiveDeviceServiceAtSiteAsync(context.DeviceServiceIdentityId.Value, context.SiteId.Value, now, cancellationToken))
            {
                return Failure(403, HumanAuthenticationOutcomes.Forbidden, "APT_DEVICE_TRUST_REQUIRED", context.CorrelationId);
            }
        }
        else if (context.DeviceServiceIdentityId.HasValue)
        {
            return Failure(403, HumanAuthenticationOutcomes.Forbidden, "AUDIENCE_DEVICE_MISMATCH", context.CorrelationId);
        }

        var normalized = NormalizeUsername(username);
        var loginHash = _tokens.HashPrivacyValue(normalized);
        var login = await _repository.FindLocalLoginAsync(normalized, now, cancellationToken);
        var releasedExpiredLockout = false;
        if (login?.UserStatus == "LOCKED" && login.LockoutReasonCode == "AUTHENTICATION_FAILURE" && login.LockoutExpiresAt <= now)
        {
            await _repository.ReleaseExpiredAuthenticationLockoutAsync(login.UserId, now, context.CentralPmsServiceIdentityId, cancellationToken);
            login = await _repository.FindLocalLoginAsync(normalized, now, cancellationToken);
            releasedExpiredLockout = true;
        }

        var recentFailures = await _repository.CountRecentFailedAttemptsAsync(login?.UserId, loginHash, context.SourceIpHash, "PASSWORD", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
        if (!releasedExpiredLockout && recentFailures >= _options.MaximumFailures)
        {
            await RecordAttemptAsync(login?.UserId, loginHash, "PASSWORD", "THROTTLED", audience, "FAILURE_WINDOW_EXCEEDED", context, now, cancellationToken);
            return Failure(429, HumanAuthenticationOutcomes.Throttled, "AUTHENTICATION_THROTTLED", context.CorrelationId, true);
        }

        var passwordValid = await VerifyPasswordSafelyAsync(password, login?.Credential, cancellationToken);

        if (login is null || !passwordValid || !IsUsable(login, now))
        {
            await RecordAttemptAsync(login?.UserId, loginHash, "PASSWORD", "INVALID", audience, "INVALID_CREDENTIALS", context, now, cancellationToken);
            if (login is not null && login.UserStatus == "ACTIVE" && recentFailures + 1 >= _options.MaximumFailures)
            {
                await _repository.ApplyAuthenticationLockoutAsync(login.UserId, now, now.AddMinutes(_options.LockoutMinutes), "AUTHENTICATION_FAILURE", context.CentralPmsServiceIdentityId, cancellationToken);
                await RecordSecurityAsync("ACCOUNT_LOCKED", "BLOCKED", "AUTHENTICATION_FAILURE", login.UserId, login.UserId, context, now, cancellationToken);
            }
            await RecordSecurityAsync("LOGIN_FAILED", "FAILED", "INVALID_CREDENTIALS", login?.UserId, login?.UserId, context, now, cancellationToken);
            return Failure(401, HumanAuthenticationOutcomes.InvalidCredentials, "INVALID_CREDENTIALS", context.CorrelationId);
        }

        var credential = login.Credential!;
        await RecordAttemptAsync(login.UserId, loginHash, "PASSWORD", "SUCCESS", audience, "PASSWORD_VERIFIED", context, now, cancellationToken);

        if (_passwords.NeedsUpgrade(credential))
        {
            var upgraded = await _passwords.HashAsync(password, cancellationToken);
            await _repository.UpdateCredentialVerifierAsync(credential.LocalCredentialId, credential.RowVersion, upgraded, context.CentralPmsServiceIdentityId, now, cancellationToken);
            credential = credential with { RowVersion = credential.RowVersion + 1 };
        }

        var passwordChangeRequired = credential.Status == "CHANGE_REQUIRED";
        var mfaRequired = audience == HumanSessionAudiences.ManagementPlatform && login.HasPrivilegedRole;
        var mfaSatisfied = false;
        Guid? mfaAuthenticatorId = null;
        DateTimeOffset? mfaVerifiedAt = null;

        if (mfaRequired)
        {
            var authenticator = login.TotpAuthenticator;
            if (authenticator is null || authenticator.Status is "PENDING_ENROLLMENT" or "RESET_REQUIRED")
            {
                return await IssueSessionAsync(login, credential, audience, context, now, passwordChangeRequired, true, false, null, null, HumanAuthenticationOutcomes.MfaEnrollmentRequired, cancellationToken);
            }

            if (authenticator.Status != "ACTIVE")
            {
                return Failure(403, HumanAuthenticationOutcomes.AccountUnavailable, "MFA_UNAVAILABLE", context.CorrelationId);
            }

            if (!_totpProtector.IsConfigured)
            {
                return Failure(503, HumanAuthenticationOutcomes.AccountUnavailable, "TOTP_PROTECTION_UNAVAILABLE", context.CorrelationId, true);
            }

            if (string.IsNullOrWhiteSpace(totpCode))
            {
                return Failure(401, HumanAuthenticationOutcomes.MfaRequired, "TOTP_REQUIRED", context.CorrelationId);
            }

            var totpFailures = await _repository.CountRecentFailedAttemptsAsync(login.UserId, loginHash, context.SourceIpHash, "TOTP", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
            if (totpFailures >= _options.MaximumFailures)
            {
                await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "THROTTLED", audience, "TOTP_FAILURE_WINDOW_EXCEEDED", context, now, cancellationToken);
                await RecordSecurityAsync("TOTP_THROTTLED", "BLOCKED", "TOTP_FAILURE_WINDOW_EXCEEDED", login.UserId, login.UserId, context, now, cancellationToken);
                return Failure(429, HumanAuthenticationOutcomes.Throttled, "TOTP_THROTTLED", context.CorrelationId, true);
            }

            var verification = VerifyTotp(login.UserId, authenticator, totpCode, now);
            if (!verification.Succeeded || !verification.MatchedTimeStep.HasValue ||
                !await _repository.TryRecordTotpSuccessAsync(authenticator.AuthenticatorId, authenticator.RowVersion, verification.MatchedTimeStep.Value, now, context.CentralPmsServiceIdentityId, cancellationToken))
            {
                await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "INVALID", audience, "TOTP_INVALID_OR_REPLAYED", context, now, cancellationToken);
                await RecordSecurityAsync("TOTP_VERIFICATION_FAILED", "FAILED", "TOTP_INVALID_OR_REPLAYED", login.UserId, login.UserId, context, now, cancellationToken);
                return Failure(401, HumanAuthenticationOutcomes.MfaRequired, "TOTP_INVALID", context.CorrelationId);
            }

            await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "SUCCESS", audience, "TOTP_VERIFIED", context, now, cancellationToken);
            await RecordSecurityAsync("TOTP_VERIFICATION_SUCCEEDED", "ALLOWED", "TOTP_VERIFIED", login.UserId, login.UserId, context, now, cancellationToken);
            mfaSatisfied = true;
            mfaAuthenticatorId = authenticator.AuthenticatorId;
            mfaVerifiedAt = now;
        }

        return await IssueSessionAsync(
            login, credential, audience, context, now, passwordChangeRequired, mfaRequired,
            mfaSatisfied, mfaAuthenticatorId, mfaVerifiedAt,
            passwordChangeRequired ? HumanAuthenticationOutcomes.PasswordChangeRequired : HumanAuthenticationOutcomes.Authenticated,
            cancellationToken);
    }

    public async Task<HumanAuthenticationResult> ResolveSessionAsync(string token, string? expectedAudience, Guid? expectedDeviceServiceIdentityId, HumanAuthenticationContext context, bool touch, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, expectedAudience, expectedDeviceServiceIdentityId, context, touch, cancellationToken);
        if (validation.Record is null) return validation.Failure!;
        return await SuccessFromSessionAsync(validation.Record, validation.Credential!, context.CorrelationId, cancellationToken);
    }

    public async Task<HumanAuthenticationResult> ContinueSessionAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, null, context.DeviceServiceIdentityId, context, false, cancellationToken);
        if (validation.Record is null) return validation.Failure!;
        var record = validation.Record;
        var now = _timeProvider.GetUtcNow();
        var credential = _tokens.Create();
        var issue = await _repository.RotateSessionAsync(record, credential, record.MfaRequirementSatisfied,
            record.MfaAuthenticatorId, record.MfaVerifiedAt, record.AssuranceContext, now,
            ComputeIdleExpiry(record.Audience, now, record.AbsoluteExpiresAt), record.AbsoluteExpiresAt,
            context.CorrelationId, cancellationToken);
        if (issue is null) return Failure(409, HumanAuthenticationOutcomes.SessionInvalid, "SESSION_ROTATION_CONFLICT", context.CorrelationId, true);
        await RecordSecurityAsync("SESSION_REVOKED", "ALLOWED", "SESSION_ROTATED", record.HumanSessionId, record.UserId, context, now, cancellationToken);
        return await SuccessFromSessionAsync(issue.Record, credential, context.CorrelationId, cancellationToken);
    }

    public async Task<HumanAuthenticationResult> LogoutAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, null, context.DeviceServiceIdentityId, context, false, cancellationToken);
        if (validation.Record is null) return validation.Failure!;
        var now = _timeProvider.GetUtcNow();
        await _repository.RevokeSessionAsync(validation.Record.HumanSessionId, validation.Record.UserId, "USER_LOGOUT", now, cancellationToken);
        await RecordSecurityAsync("LOGOUT_COMPLETED", "ALLOWED", "USER_LOGOUT", validation.Record.HumanSessionId, validation.Record.UserId, context, now, cancellationToken);
        return Failure(200, "LOGGED_OUT", null, context.CorrelationId);
    }

    public async Task<HumanAuthenticationResult> LogoutAllAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, null, context.DeviceServiceIdentityId, context, false, cancellationToken);
        if (validation.Record is null) return validation.Failure!;
        var now = _timeProvider.GetUtcNow();
        await _repository.RevokeAllUserSessionsAsync(validation.Record.UserId, validation.Record.UserId, "GLOBAL_LOGOUT", now, null, cancellationToken);
        await RecordSecurityAsync("SESSION_REVOKED", "ALLOWED", "GLOBAL_LOGOUT", validation.Record.UserId, validation.Record.UserId, context, now, cancellationToken);
        return Failure(200, "LOGGED_OUT_ALL", null, context.CorrelationId);
    }

    public async Task<HumanAuthenticationResult> FreshAuthenticateAsync(string token, string password, string? totpCode, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, null, context.DeviceServiceIdentityId, context, false, cancellationToken);
        if (validation.Record is null) return validation.Failure!;
        var record = validation.Record;
        var now = _timeProvider.GetUtcNow();
        var loginHash = _tokens.HashPrivacyValue(NormalizeUsername(record.Username));
        var login = await _repository.FindLocalLoginAsync(NormalizeUsername(record.Username), now, cancellationToken);
        var passwordFailures = await _repository.CountRecentFailedAttemptsAsync(record.UserId, loginHash,
            context.SourceIpHash, "PASSWORD", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
        if (passwordFailures >= _options.MaximumFailures)
        {
            await RecordAttemptAsync(record.UserId, loginHash, "PASSWORD", "THROTTLED", record.Audience,
                "FRESH_AUTHENTICATION_THROTTLED", context, now, cancellationToken);
            return Failure(429, HumanAuthenticationOutcomes.Throttled, "AUTHENTICATION_THROTTLED", context.CorrelationId, true);
        }
        if (login?.Credential is null || !await VerifyPasswordSafelyAsync(password, login.Credential, cancellationToken))
        {
            await RecordAttemptAsync(record.UserId, loginHash, "PASSWORD", "INVALID", record.Audience, "FRESH_AUTHENTICATION_FAILED", context, now, cancellationToken);
            if (passwordFailures + 1 >= _options.MaximumFailures)
            {
                await _repository.ApplyAuthenticationLockoutAsync(record.UserId, now, now.AddMinutes(_options.LockoutMinutes),
                    "AUTHENTICATION_FAILURE", context.CentralPmsServiceIdentityId, cancellationToken);
                await RecordSecurityAsync("ACCOUNT_LOCKED", "BLOCKED", "AUTHENTICATION_FAILURE", record.UserId,
                    record.UserId, context, now, cancellationToken);
            }
            await RecordSecurityAsync("LOGIN_FAILED", "FAILED", "FRESH_AUTHENTICATION_FAILED", record.UserId, record.UserId, context, now, cancellationToken);
            return Failure(401, HumanAuthenticationOutcomes.InvalidCredentials, "FRESH_AUTHENTICATION_FAILED", context.CorrelationId);
        }

        var mfaSatisfied = false;
        Guid? mfaAuthenticatorId = null;
        DateTimeOffset? mfaVerifiedAt = null;
        if (record.Audience == HumanSessionAudiences.ManagementPlatform && login.HasPrivilegedRole)
        {
            var authenticator = login.TotpAuthenticator;
            if (!_totpProtector.IsConfigured)
            {
                return Failure(503, HumanAuthenticationOutcomes.AccountUnavailable, "TOTP_PROTECTION_UNAVAILABLE", context.CorrelationId, true);
            }
            if (authenticator?.Status != "ACTIVE" || string.IsNullOrWhiteSpace(totpCode))
            {
                return Failure(401, HumanAuthenticationOutcomes.MfaRequired, "TOTP_REQUIRED", context.CorrelationId);
            }
            var failures = await _repository.CountRecentFailedAttemptsAsync(login.UserId, loginHash, context.SourceIpHash, "TOTP", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
            if (failures >= _options.MaximumFailures)
            {
                await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "THROTTLED", record.Audience, "TOTP_FAILURE_WINDOW_EXCEEDED", context, now, cancellationToken);
                await RecordSecurityAsync("TOTP_THROTTLED", "BLOCKED", "TOTP_FAILURE_WINDOW_EXCEEDED", login.UserId, login.UserId, context, now, cancellationToken);
                return Failure(429, HumanAuthenticationOutcomes.Throttled, "TOTP_THROTTLED", context.CorrelationId, true);
            }
            var verification = VerifyTotp(login.UserId, authenticator, totpCode, now);
            if (!verification.Succeeded || !verification.MatchedTimeStep.HasValue ||
                !await _repository.TryRecordTotpSuccessAsync(authenticator.AuthenticatorId, authenticator.RowVersion, verification.MatchedTimeStep.Value, now, context.CentralPmsServiceIdentityId, cancellationToken))
            {
                await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "INVALID", record.Audience, "TOTP_INVALID_OR_REPLAYED", context, now, cancellationToken);
                await RecordSecurityAsync("TOTP_VERIFICATION_FAILED", "FAILED", "TOTP_INVALID_OR_REPLAYED", login.UserId, login.UserId, context, now, cancellationToken);
                return Failure(401, HumanAuthenticationOutcomes.MfaRequired, "TOTP_INVALID", context.CorrelationId);
            }
            await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "SUCCESS", record.Audience, "TOTP_VERIFIED", context, now, cancellationToken);
            await RecordSecurityAsync("TOTP_VERIFICATION_SUCCEEDED", "ALLOWED", "TOTP_VERIFIED", login.UserId, login.UserId, context, now, cancellationToken);
            mfaSatisfied = true;
            mfaAuthenticatorId = authenticator.AuthenticatorId;
            mfaVerifiedAt = now;
        }

        await RecordAttemptAsync(login.UserId, loginHash, "PASSWORD", "SUCCESS", record.Audience, "FRESH_PASSWORD_VERIFIED", context, now, cancellationToken);
        var replacement = _tokens.Create();
        var absolute = now.AddHours(record.Audience == HumanSessionAudiences.Apt ? _options.AptAbsoluteHours : _options.WebAbsoluteHours);
        var assurance = mfaSatisfied ? "PASSWORD_TOTP" : "PASSWORD";
        var issue = await _repository.RotateSessionAsync(record, replacement, mfaSatisfied, mfaAuthenticatorId,
            mfaVerifiedAt, assurance, now, ComputeIdleExpiry(record.Audience, now, absolute), absolute,
            context.CorrelationId, cancellationToken);
        if (issue is null) return Failure(409, HumanAuthenticationOutcomes.SessionInvalid, "SESSION_ROTATION_CONFLICT", context.CorrelationId, true);
        await RecordSecurityAsync("LOGIN_SUCCEEDED", "ALLOWED", "FRESH_REAUTHENTICATION", record.UserId, record.UserId, context, now, cancellationToken);
        return await SuccessFromSessionAsync(issue.Record, replacement, context.CorrelationId, cancellationToken);
    }

    public async Task<HumanAuthenticationResult> ChangePasswordAsync(string token, string currentPassword, string newPassword, string? totpCode, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, null, context.DeviceServiceIdentityId, context, false, cancellationToken);
        if (validation.Record is null) return validation.Failure!;
        var record = validation.Record;
        var now = _timeProvider.GetUtcNow();
        var loginHash = _tokens.HashPrivacyValue(NormalizeUsername(record.Username));
        var login = await _repository.FindLocalLoginAsync(NormalizeUsername(record.Username), now, cancellationToken);
        var passwordFailures = await _repository.CountRecentFailedAttemptsAsync(record.UserId, loginHash,
            context.SourceIpHash, "PASSWORD", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
        if (passwordFailures >= _options.MaximumFailures)
        {
            await RecordAttemptAsync(record.UserId, loginHash, "PASSWORD", "THROTTLED", record.Audience,
                "PASSWORD_CHANGE_THROTTLED", context, now, cancellationToken);
            return Failure(429, HumanAuthenticationOutcomes.Throttled, "AUTHENTICATION_THROTTLED", context.CorrelationId, true);
        }
        if (login?.Credential is null || !await VerifyPasswordSafelyAsync(currentPassword, login.Credential, cancellationToken))
        {
            await RecordAttemptAsync(record.UserId, loginHash, "PASSWORD", "INVALID", record.Audience,
                "CURRENT_PASSWORD_INVALID", context, now, cancellationToken);
            if (passwordFailures + 1 >= _options.MaximumFailures)
            {
                await _repository.ApplyAuthenticationLockoutAsync(record.UserId, now, now.AddMinutes(_options.LockoutMinutes),
                    "AUTHENTICATION_FAILURE", context.CentralPmsServiceIdentityId, cancellationToken);
                await RecordSecurityAsync("ACCOUNT_LOCKED", "BLOCKED", "AUTHENTICATION_FAILURE", record.UserId,
                    record.UserId, context, now, cancellationToken);
            }
            await RecordSecurityAsync("LOGIN_FAILED", "FAILED", "CURRENT_PASSWORD_INVALID", record.UserId,
                record.UserId, context, now, cancellationToken);
            return Failure(401, HumanAuthenticationOutcomes.InvalidCredentials, "CURRENT_PASSWORD_INVALID", context.CorrelationId);
        }
        var mfaSatisfied = false;
        Guid? mfaAuthenticatorId = null;
        DateTimeOffset? mfaVerifiedAt = null;
        if (record.Audience == HumanSessionAudiences.ManagementPlatform && login.HasPrivilegedRole)
        {
            var authenticator = login.TotpAuthenticator;
            if (!_totpProtector.IsConfigured) return Failure(503, HumanAuthenticationOutcomes.AccountUnavailable, "TOTP_PROTECTION_UNAVAILABLE", context.CorrelationId, true);
            if (authenticator?.Status != "ACTIVE" || string.IsNullOrWhiteSpace(totpCode)) return Failure(401, HumanAuthenticationOutcomes.MfaRequired, "TOTP_REQUIRED", context.CorrelationId);
            var totpFailures = await _repository.CountRecentFailedAttemptsAsync(login.UserId, loginHash,
                context.SourceIpHash, "TOTP", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
            if (totpFailures >= _options.MaximumFailures)
            {
                await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "THROTTLED", record.Audience,
                    "TOTP_FAILURE_WINDOW_EXCEEDED", context, now, cancellationToken);
                await RecordSecurityAsync("TOTP_THROTTLED", "BLOCKED", "TOTP_FAILURE_WINDOW_EXCEEDED", login.UserId,
                    login.UserId, context, now, cancellationToken);
                return Failure(429, HumanAuthenticationOutcomes.Throttled, "TOTP_THROTTLED", context.CorrelationId, true);
            }
            var verification = VerifyTotp(login.UserId, authenticator, totpCode, now);
            if (!verification.Succeeded || !verification.MatchedTimeStep.HasValue ||
                !await _repository.TryRecordTotpSuccessAsync(authenticator.AuthenticatorId, authenticator.RowVersion, verification.MatchedTimeStep.Value, now, context.CentralPmsServiceIdentityId, cancellationToken))
            {
                await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "INVALID", record.Audience,
                    "TOTP_INVALID_OR_REPLAYED", context, now, cancellationToken);
                await RecordSecurityAsync("TOTP_VERIFICATION_FAILED", "FAILED", "TOTP_INVALID_OR_REPLAYED", login.UserId,
                    login.UserId, context, now, cancellationToken);
                return Failure(401, HumanAuthenticationOutcomes.MfaRequired, "TOTP_INVALID", context.CorrelationId);
            }
            await RecordAttemptAsync(login.UserId, loginHash, "TOTP", "SUCCESS", record.Audience,
                "TOTP_VERIFIED", context, now, cancellationToken);
            await RecordSecurityAsync("TOTP_VERIFICATION_SUCCEEDED", "ALLOWED", "TOTP_VERIFIED", login.UserId,
                login.UserId, context, now, cancellationToken);
            mfaSatisfied = true;
            mfaAuthenticatorId = authenticator.AuthenticatorId;
            mfaVerifiedAt = now;
        }

        PasswordHashMaterial material;
        try { material = await _passwords.HashAsync(newPassword, cancellationToken); }
        catch (ArgumentException) { return Failure(400, "PASSWORD_REJECTED", "PASSWORD_POLICY_FAILED", context.CorrelationId); }
        var replacement = _tokens.Create();
        var absolute = now.AddHours(record.Audience == HumanSessionAudiences.Apt ? _options.AptAbsoluteHours : _options.WebAbsoluteHours);
        var issue = await _repository.ChangePasswordAndRotateSessionAsync(record, login.Credential.RowVersion,
            material, replacement, mfaSatisfied, mfaAuthenticatorId, mfaVerifiedAt,
            mfaSatisfied ? "PASSWORD_TOTP" : "PASSWORD", now,
            ComputeIdleExpiry(record.Audience, now, absolute), absolute, context.CorrelationId, cancellationToken);
        if (issue is null) return Failure(409, HumanAuthenticationOutcomes.SessionInvalid, "CREDENTIAL_CHANGE_CONFLICT", context.CorrelationId, true);
        await RecordSecurityAsync("CREDENTIAL_CHANGED", "ALLOWED", "PASSWORD_CHANGED", record.UserId, record.UserId, context, now, cancellationToken);
        return await SuccessFromSessionAsync(issue.Record, replacement, context.CorrelationId, cancellationToken, "PASSWORD_CHANGED");
    }

    public async Task<TotpEnrollmentResult> BeginTotpEnrollmentAsync(string token, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, HumanSessionAudiences.ManagementPlatform, null, context, false, cancellationToken);
        if (validation.Record is null) return TotpFailure(validation.Failure!.HttpStatusCode, validation.Failure.Response.ErrorCode ?? "SESSION_INVALID", context.CorrelationId);
        var record = validation.Record;
        if (!record.HasPrivilegedRole) return TotpFailure(403, "MFA_ENROLLMENT_NOT_REQUIRED", context.CorrelationId);
        if (!_totpProtector.IsConfigured) return TotpFailure(503, "TOTP_PROTECTION_UNAVAILABLE", context.CorrelationId);
        var existing = await _repository.GetCurrentTotpAuthenticatorAsync(record.UserId, cancellationToken);
        if (existing?.Status is "PENDING_ENROLLMENT" or "ACTIVE" or "SUSPENDED") return TotpFailure(409, "TOTP_AUTHENTICATOR_ALREADY_EXISTS", context.CorrelationId);

        var secret = _totp.GenerateSecret();
        var authenticatorId = Guid.NewGuid();
        try
        {
            var envelope = _totpProtector.Protect(record.UserId, authenticatorId, secret);
            var now = _timeProvider.GetUtcNow();
            var created = await _repository.CreatePendingTotpAuthenticatorAsync(authenticatorId, record.UserId, envelope, _totpProtector.KeyReference, _totpProtector.KeyVersion, _totpProtector.EnvelopeFormatVersion, now, record.UserId, cancellationToken);
            if (created is null) return TotpFailure(409, "TOTP_ENROLLMENT_CONFLICT", context.CorrelationId);
            await RecordSecurityAsync("TOTP_ENROLLMENT_STARTED", "ALLOWED", "USER_ENROLLMENT", authenticatorId, record.UserId, context, now, cancellationToken);
            return new TotpEnrollmentResult(200, new TotpEnrollmentResponse("TOTP_ENROLLMENT_STARTED", _totp.EncodeSecret(secret), _totp.BuildProvisioningUri(record.Username, secret), now, context.CorrelationId));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public async Task<TotpEnrollmentResult> ConfirmTotpEnrollmentAsync(string token, string code, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var validation = await ValidateSessionAsync(token, HumanSessionAudiences.ManagementPlatform, null, context, false, cancellationToken);
        if (validation.Record is null) return TotpFailure(validation.Failure!.HttpStatusCode, validation.Failure.Response.ErrorCode ?? "SESSION_INVALID", context.CorrelationId);
        var record = validation.Record;
        var authenticator = await _repository.GetCurrentTotpAuthenticatorAsync(record.UserId, cancellationToken);
        if (authenticator?.Status != "PENDING_ENROLLMENT") return TotpFailure(409, "TOTP_ENROLLMENT_NOT_PENDING", context.CorrelationId);
        var now = _timeProvider.GetUtcNow();
        var loginHash = _tokens.HashPrivacyValue(NormalizeUsername(record.Username));
        var failures = await _repository.CountRecentFailedAttemptsAsync(record.UserId, loginHash,
            context.SourceIpHash, "TOTP", now.AddMinutes(-_options.FailureWindowMinutes), cancellationToken);
        if (failures >= _options.MaximumFailures)
        {
            await RecordAttemptAsync(record.UserId, loginHash, "TOTP", "THROTTLED", record.Audience,
                "TOTP_ENROLLMENT_THROTTLED", context, now, cancellationToken);
            await RecordSecurityAsync("TOTP_THROTTLED", "BLOCKED", "TOTP_ENROLLMENT_THROTTLED",
                authenticator.AuthenticatorId, record.UserId, context, now, cancellationToken);
            return TotpFailure(429, "TOTP_THROTTLED", context.CorrelationId);
        }
        var verification = VerifyTotp(record.UserId, authenticator, code, now);
        if (!verification.Succeeded || !verification.MatchedTimeStep.HasValue ||
            !await _repository.ConfirmTotpAuthenticatorAsync(authenticator.AuthenticatorId, authenticator.RowVersion, verification.MatchedTimeStep.Value, now, record.UserId, cancellationToken))
        {
            await RecordAttemptAsync(record.UserId, loginHash, "TOTP", "INVALID", record.Audience,
                "ENROLLMENT_CONFIRMATION_FAILED", context, now, cancellationToken);
            await RecordSecurityAsync("TOTP_VERIFICATION_FAILED", "FAILED", "ENROLLMENT_CONFIRMATION_FAILED", authenticator.AuthenticatorId, record.UserId, context, now, cancellationToken);
            return TotpFailure(400, "TOTP_CONFIRMATION_FAILED", context.CorrelationId);
        }
        await RecordAttemptAsync(record.UserId, loginHash, "TOTP", "SUCCESS", record.Audience,
            "ENROLLMENT_CONFIRMED", context, now, cancellationToken);
        await RecordSecurityAsync("TOTP_CONFIRMED", "ALLOWED", "ENROLLMENT_CONFIRMED", authenticator.AuthenticatorId, record.UserId, context, now, cancellationToken);
        return new TotpEnrollmentResult(200, new TotpEnrollmentResponse("TOTP_CONFIRMED_REAUTHENTICATION_REQUIRED", null, null, now, context.CorrelationId));
    }

    public async Task RequestPasswordResetAsync(string username, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var login = await _repository.FindLocalLoginAsync(NormalizeUsername(username), now, cancellationToken);
        if (_challengeDelivery.Enabled && login is not null && login.UserStatus is "ACTIVE" or "LOCKED")
        {
            var expiresAt = now.AddMinutes(_options.CredentialChallengeMinutes);
            var challenge = await _repository.CreateCredentialChallengeAsync(login.UserId, "PASSWORD_RESET", now, expiresAt, context.CentralPmsServiceIdentityId, context.CorrelationId, cancellationToken);
            try
            {
                await _challengeDelivery.DeliverAsync(new CredentialChallengeDeliveryRequest(
                    login.UserId, "PASSWORD_RESET", challenge.Reference, challenge.Secret, expiresAt,
                    context.CorrelationId), cancellationToken);
            }
            catch
            {
                await _repository.RevokeCredentialChallengeAsync(challenge.Reference,
                    context.CentralPmsServiceIdentityId, "DELIVERY_FAILED", now, cancellationToken);
                throw;
            }
        }
    }

    public async Task<HumanAuthenticationResult> ResetPasswordAsync(Guid challengeReference, string challengeSecret, string newPassword, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        PasswordHashMaterial material;
        try { material = await _passwords.HashAsync(newPassword, cancellationToken); }
        catch (ArgumentException) { return Failure(400, "PASSWORD_REJECTED", "PASSWORD_POLICY_FAILED", context.CorrelationId); }
        var now = _timeProvider.GetUtcNow();
        var completedUserId = await _repository.CompleteCredentialChallengeAsync(challengeReference,
            _tokens.HashSecret(challengeSecret), "PASSWORD_RESET", material, now,
            context.CentralPmsServiceIdentityId, cancellationToken);
        if (!completedUserId.HasValue) return Failure(400, "CHALLENGE_REJECTED", "INVALID_OR_EXPIRED_CHALLENGE", context.CorrelationId);
        await RecordSecurityAsync("CREDENTIAL_RESET", "ALLOWED", "PASSWORD_RESET_COMPLETED", completedUserId, completedUserId, context, now, cancellationToken);
        return Failure(200, "PASSWORD_RESET_COMPLETED", null, context.CorrelationId);
    }

    public async Task<HumanAuthenticationResult> ActivateAsync(Guid challengeReference, string challengeSecret, string newPassword, HumanAuthenticationContext context, CancellationToken cancellationToken)
    {
        PasswordHashMaterial material;
        try { material = await _passwords.HashAsync(newPassword, cancellationToken); }
        catch (ArgumentException) { return Failure(400, "PASSWORD_REJECTED", "PASSWORD_POLICY_FAILED", context.CorrelationId); }
        var now = _timeProvider.GetUtcNow();
        var completedUserId = await _repository.CompleteCredentialChallengeAsync(challengeReference,
            _tokens.HashSecret(challengeSecret), "ACCOUNT_ACTIVATION", material, now,
            context.CentralPmsServiceIdentityId, cancellationToken);
        if (!completedUserId.HasValue) return Failure(400, "CHALLENGE_REJECTED", "INVALID_OR_EXPIRED_CHALLENGE", context.CorrelationId);
        await RecordSecurityAsync("ACTIVATION_CHALLENGE_CONSUMED", "ALLOWED", "ACCOUNT_ACTIVATED", completedUserId, completedUserId, context, now, cancellationToken);
        return Failure(200, "ACCOUNT_ACTIVATED", null, context.CorrelationId);
    }

    public async Task ResetTotpAsync(Guid targetUserId, Guid actorUserId, string reasonCode, Guid correlationId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await _repository.ResetTotpAuthenticatorAsync(targetUserId, actorUserId, reasonCode, now, cancellationToken);
        var context = new HumanAuthenticationContext(correlationId, _options.CentralPmsServiceIdentityId, null, null, null, null);
        await RecordSecurityAsync("TOTP_RESET", "ALLOWED", reasonCode, targetUserId, actorUserId, context, now, cancellationToken);
    }

    public async Task<bool> ChangeTotpAsync(
        Guid targetUserId,
        long expectedRowVersion,
        string action,
        Guid actorUserId,
        string reasonCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedAction = action.Trim().ToUpperInvariant();
        if (normalizedAction is not ("RESET" or "REMOVE"))
        {
            throw new ArgumentException("The TOTP administration action is invalid.", nameof(action));
        }

        var now = _timeProvider.GetUtcNow();
        var changed = await _repository.ChangeTotpAuthenticatorAsync(
            targetUserId, expectedRowVersion, normalizedAction, actorUserId, reasonCode, correlationId,
            _options.CentralPmsServiceIdentityId, now, cancellationToken);
        return changed;
    }

    private async Task<HumanAuthenticationResult> IssueSessionAsync(HumanLoginRecord login, LocalCredentialRecord credential, string audience, HumanAuthenticationContext context, DateTimeOffset now, bool passwordChangeRequired, bool mfaRequired, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string outcome, CancellationToken cancellationToken)
    {
        var token = _tokens.Create();
        var absolute = now.AddHours(audience == HumanSessionAudiences.Apt ? _options.AptAbsoluteHours : _options.WebAbsoluteHours);
        var idle = ComputeIdleExpiry(audience, now, absolute);
        var assurance = passwordChangeRequired ? "PASSWORD_CHANGE_REQUIRED" : mfaSatisfied ? "PASSWORD_TOTP" : mfaRequired ? "PASSWORD_MFA_PENDING" : "PASSWORD";
        var issue = await _repository.CreateSessionAsync(login.UserId, credential.LocalCredentialId, audience, context.DeviceServiceIdentityId, mfaSatisfied, mfaAuthenticatorId, mfaVerifiedAt, assurance, login.CredentialVersion, login.AuthorizationEpoch, now, idle, absolute, context.CorrelationId, token, cancellationToken);
        await RecordSecurityAsync("LOGIN_SUCCEEDED", "ALLOWED", outcome, login.UserId, login.UserId, context, now, cancellationToken);
        return await SuccessFromSessionAsync(issue.Record, token, context.CorrelationId, cancellationToken, outcome, passwordChangeRequired, mfaRequired);
    }

    private async Task<HumanAuthenticationResult> SuccessFromSessionAsync(HumanSessionRecord record, SessionCredential credential, Guid correlationId, CancellationToken cancellationToken, string outcome = HumanAuthenticationOutcomes.Authenticated, bool? passwordChangeRequired = null, bool? mfaRequired = null)
    {
        var authorization = await _repository.GetEffectiveAuthorizationAsync(record.UserId, _timeProvider.GetUtcNow(), cancellationToken);
        var requiresMfa = mfaRequired ?? (record.Audience == HumanSessionAudiences.ManagementPlatform && record.HasPrivilegedRole);
        var restricted = (requiresMfa && !record.MfaRequirementSatisfied) || record.LocalCredentialStatus == "CHANGE_REQUIRED";
        var dto = new HumanSessionDto(record.SessionReference, record.UserId, record.Username, record.DisplayName, record.Audience, record.AssuranceContext,
            record.HasPrivilegedRole, passwordChangeRequired ?? record.LocalCredentialStatus == "CHANGE_REQUIRED", requiresMfa, record.MfaRequirementSatisfied,
            record.AuthenticatedAt, record.LastSeenAt, record.IdleExpiresAt, record.AbsoluteExpiresAt,
            restricted ? [] : authorization.Permissions, restricted ? [] : authorization.SiteIds,
            restricted ? [] : authorization.SiteGroupIds, !restricted && authorization.HasGlobalScope,
            record.DeviceServiceIdentityId, correlationId);
        return new HumanAuthenticationResult(200, new HumanAuthenticationResponse(outcome, true, dto,
            record.Audience == HumanSessionAudiences.Apt ? credential.SerializedToken : null, null, false, correlationId), credential, record.HumanSessionId);
    }

    private async Task<(HumanSessionRecord? Record, SessionCredential? Credential, HumanAuthenticationResult? Failure)> ValidateSessionAsync(string token, string? expectedAudience, Guid? expectedDeviceServiceIdentityId, HumanAuthenticationContext context, bool touch, CancellationToken cancellationToken)
    {
        if (!_tokens.TryParse(token, out var credential)) return (null, null, Failure(401, HumanAuthenticationOutcomes.SessionInvalid, "SESSION_INVALID", context.CorrelationId));
        var record = await _repository.FindSessionAsync(credential.SessionReference, cancellationToken);
        if (record is null || !FixedTimeHashEquals(record.SessionSecretHash, _tokens.HashSecret(credential.Secret)))
        {
            return (null, null, Failure(401, HumanAuthenticationOutcomes.SessionInvalid, "SESSION_INVALID", context.CorrelationId));
        }
        var now = _timeProvider.GetUtcNow();
        if (record.SessionStatus != "ACTIVE" || record.IdleExpiresAt <= now || record.AbsoluteExpiresAt <= now)
        {
            if (record.SessionStatus == "ACTIVE") await _repository.MarkSessionExpiredAsync(record.HumanSessionId, now, cancellationToken);
            return (null, null, Failure(401, HumanAuthenticationOutcomes.SessionExpired, "SESSION_EXPIRED", context.CorrelationId));
        }
        if (record.UserStatus != "ACTIVE" || record.UserEffectiveFrom > now || (record.UserEffectiveTo is not null && record.UserEffectiveTo <= now) ||
            record.LocalCredentialStatus is not ("ACTIVE" or "CHANGE_REQUIRED") ||
            record.CredentialVersionSnapshot != record.CurrentCredentialVersion)
        {
            await _repository.RevokeSessionAsync(record.HumanSessionId, record.UserId, "IDENTITY_OR_CREDENTIAL_CHANGED", now, cancellationToken);
            return (null, null, Failure(401, HumanAuthenticationOutcomes.SessionInvalid, "SESSION_REVOKED", context.CorrelationId));
        }
        if ((!string.IsNullOrWhiteSpace(expectedAudience) && record.Audience != NormalizeAudience(expectedAudience)) ||
            (record.Audience == HumanSessionAudiences.Apt && (!expectedDeviceServiceIdentityId.HasValue || record.DeviceServiceIdentityId != expectedDeviceServiceIdentityId)))
        {
            return (null, null, Failure(403, HumanAuthenticationOutcomes.Forbidden, "SESSION_BINDING_MISMATCH", context.CorrelationId));
        }
        if (touch)
        {
            var nextIdle = ComputeIdleExpiry(record.Audience, now, record.AbsoluteExpiresAt);
            if (await _repository.TouchSessionAsync(record.HumanSessionId, record.RowVersion, now, nextIdle, cancellationToken))
            {
                record = record with { LastSeenAt = now, IdleExpiresAt = nextIdle, RowVersion = record.RowVersion + 1 };
            }
        }
        return (record, credential, null);
    }

    private TotpVerificationResult VerifyTotp(Guid userId, TotpAuthenticatorRecord authenticator, string code, DateTimeOffset now)
    {
        byte[]? secret = null;
        try
        {
            secret = _totpProtector.Unprotect(userId, authenticator.AuthenticatorId, authenticator);
            return _totp.Verify(secret, code, now);
        }
        catch (CryptographicException)
        {
            return new TotpVerificationResult(false, null);
        }
        catch (InvalidOperationException)
        {
            return new TotpVerificationResult(false, null);
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
        }
    }

    private async Task<bool> VerifyPasswordSafelyAsync(string password, LocalCredentialRecord? credential, CancellationToken cancellationToken)
    {
        try { return await _passwords.VerifyAsync(password, credential, cancellationToken); }
        catch (ArgumentException) { return false; }
    }

    private static bool IsUsable(HumanLoginRecord login, DateTimeOffset now) =>
        login.UserStatus == "ACTIVE" && login.EffectiveFrom <= now && (login.EffectiveTo is null || login.EffectiveTo > now) &&
        login.Credential is { Status: "ACTIVE" or "CHANGE_REQUIRED" };

    private DateTimeOffset ComputeIdleExpiry(string audience, DateTimeOffset now, DateTimeOffset absolute) =>
        Min(now.AddMinutes(audience == HumanSessionAudiences.Apt ? _options.AptIdleMinutes : _options.WebIdleMinutes), absolute);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
    private static string NormalizeUsername(string value) => value.Trim().ToLowerInvariant();
    private static string NormalizeAudience(string value) => value.Trim().Replace('-', '_').ToUpperInvariant();

    private static bool FixedTimeHashEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private Task RecordAttemptAsync(Guid? userId, string loginHash, string type, string result, string audience, string reason, HumanAuthenticationContext context, DateTimeOffset now, CancellationToken cancellationToken) =>
        _repository.RecordAuthenticationAttemptAsync(userId, loginHash, context.SourceIpHash, context.UserAgentHash, type, result, audience, reason, now, context.CorrelationId, context.CentralPmsServiceIdentityId, cancellationToken);

    private Task RecordSecurityAsync(string type, string result, string reason, Guid? targetId, Guid? actorId, HumanAuthenticationContext context, DateTimeOffset now, CancellationToken cancellationToken) =>
        _repository.RecordSecurityEventAsync(type, result, reason, targetId, actorId, context.SourceIpHash, context.UserAgentHash, context.CorrelationId, context.CentralPmsServiceIdentityId, now, cancellationToken);

    private static HumanAuthenticationResult Failure(int status, string outcome, string? errorCode, Guid correlationId, bool retryable = false) =>
        new(status, new HumanAuthenticationResponse(outcome, false, null, null, errorCode, retryable, correlationId));

    private static TotpEnrollmentResult TotpFailure(int status, string errorCode, Guid correlationId) =>
        new(status, new TotpEnrollmentResponse("REJECTED", null, null, null, correlationId, errorCode));
}
