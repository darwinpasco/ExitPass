using System.Net.Mail;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.HumanAuthentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class HumanAuthenticationAdministrationGateway : IHumanAuthenticationAdministrationGateway
{
    private readonly IHumanAuthenticationRepository _authenticationRepository;
    private readonly IHumanMfaAdministrationService _mfaAdministration;
    private readonly ICredentialChallengeDelivery _challengeDelivery;
    private readonly ICredentialChallengeLinkBuilder _challengeLinks;
    private readonly IManagementPlatformIdentityAdministrationRepository _identityAdministration;
    private readonly HumanAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    public HumanAuthenticationAdministrationGateway(
        IHumanAuthenticationRepository authenticationRepository,
        IHumanMfaAdministrationService mfaAdministration,
        ICredentialChallengeDelivery challengeDelivery,
        ICredentialChallengeLinkBuilder challengeLinks,
        IManagementPlatformIdentityAdministrationRepository identityAdministration,
        IOptions<HumanAuthenticationOptions> options,
        TimeProvider timeProvider)
    {
        _authenticationRepository = authenticationRepository;
        _mfaAdministration = mfaAdministration;
        _challengeDelivery = challengeDelivery;
        _challengeLinks = challengeLinks;
        _identityAdministration = identityAdministration;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public HumanAuthenticationAdministrationGateway(
        IHumanAuthenticationRepository authenticationRepository,
        IHumanMfaAdministrationService mfaAdministration,
        ICredentialChallengeDelivery challengeDelivery,
        IManagementPlatformIdentityAdministrationRepository identityAdministration,
        IOptions<HumanAuthenticationOptions> options,
        TimeProvider timeProvider)
        : this(authenticationRepository, mfaAdministration, challengeDelivery,
            new DisabledCredentialChallengeLinkBuilder(), identityAdministration, options, timeProvider)
    {
    }

    public bool EmailDeliveryEnabled => _challengeDelivery.Enabled;
    public bool ActivationLinkEnabled => _challengeLinks.Enabled;

    public async Task<IdentityAdministrationResult<CredentialResetChallengeResult>> IssueCredentialChallengeAsync(
        IdentityAdministrationActor actor,
        CreateCredentialResetChallengeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Purpose is not ("PASSWORD_RESET" or "ACCOUNT_ACTIVATION"))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Invalid,
                "INVALID_CREDENTIAL_CHALLENGE_PURPOSE", command.CorrelationId);
        }
        if (command.DeliveryMode is not (ActivationDeliveryModes.Email or ActivationDeliveryModes.AdminIssued))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Invalid,
                "INVALID_CREDENTIAL_CHALLENGE_DELIVERY_MODE", command.CorrelationId);
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = command.ExpiresAt;
        if (expiresAt <= now || expiresAt > now.AddMinutes(_options.CredentialChallengeMinutes))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Invalid,
                "INVALID_CREDENTIAL_CHALLENGE_EXPIRY", command.CorrelationId);
        }
        if (!_challengeLinks.Enabled)
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.IntegrationUnavailable,
                "ACCOUNT_LIFECYCLE_URL_NOT_CONFIGURED", command.CorrelationId);
        }

        var target = await _authenticationRepository.GetCredentialChallengeTargetAsync(command.UserReference, cancellationToken);
        if (target is null)
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.NotFound,
                "IDENTITY_USER_NOT_FOUND", command.CorrelationId);
        }
        if (command.Purpose == "ACCOUNT_ACTIVATION" && target.Status != "INVITED")
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Conflict,
                "INVITATION_NOT_PENDING", command.CorrelationId);
        }
        if (command.Purpose == "PASSWORD_RESET" && target.Status is not ("ACTIVE" or "LOCKED"))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Conflict,
                "PASSWORD_RESET_ACCOUNT_NOT_ELIGIBLE", command.CorrelationId);
        }
        if (command.Purpose == "PASSWORD_RESET" && target.UsableLocalCredentialCount != 1)
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Conflict,
                "PASSWORD_RESET_CREDENTIAL_CONFLICT", command.CorrelationId);
        }
        if (command.Purpose == "PASSWORD_RESET" && command.DeliveryMode == ActivationDeliveryModes.AdminIssued &&
            IsUsableEmail(target.Email))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Conflict,
                "ADMIN_ISSUED_PASSWORD_RECOVERY_NOT_REQUIRED", command.CorrelationId);
        }
        if (command.DeliveryMode == ActivationDeliveryModes.Email &&
            (!_challengeDelivery.Enabled || string.IsNullOrWhiteSpace(target.Email)))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.IntegrationUnavailable,
                "CREDENTIAL_CHALLENGE_EMAIL_DELIVERY_NOT_CONFIGURED", command.CorrelationId);
        }
        if (command.DeliveryMode == ActivationDeliveryModes.AdminIssued && !command.AdminIssuedHandoffAcknowledged)
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Invalid,
                "ADMIN_ISSUED_HANDOFF_ACKNOWLEDGEMENT_REQUIRED", command.CorrelationId);
        }

        var challenge = await _authenticationRepository.CreateCredentialChallengeAsync(
            command.UserReference, command.Purpose, $"{command.Purpose}_{command.DeliveryMode}", now, expiresAt,
            _options.CentralPmsServiceIdentityId, command.CorrelationId, cancellationToken);
        if (command.DeliveryMode == ActivationDeliveryModes.Email)
        {
            try
            {
                await _challengeDelivery.DeliverAsync(new CredentialChallengeDeliveryRequest(
                    command.UserReference, target.Email!, command.Purpose, challenge.Reference, challenge.Secret,
                    expiresAt, command.CorrelationId), cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                await _authenticationRepository.RevokeCredentialChallengeAsync(challenge.Reference,
                    _options.CentralPmsServiceIdentityId, $"{command.Purpose}_EMAIL_DELIVERY_FAILED", now, cancellationToken);
                await _authenticationRepository.RecordSecurityEventAsync(
                    "CREDENTIAL_CHALLENGE_DELIVERY_FAILED", "FAILED", $"{command.Purpose}_EMAIL_DELIVERY_FAILED",
                    challenge.Reference, actor.UserId, null, null, command.CorrelationId,
                    _options.CentralPmsServiceIdentityId, now, cancellationToken);
                return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.IntegrationUnavailable,
                    "CREDENTIAL_CHALLENGE_DELIVERY_FAILED", command.CorrelationId);
            }
        }

        if (command.Purpose == "ACCOUNT_ACTIVATION")
        {
            await _authenticationRepository.RecordSecurityEventAsync("ACTIVATION_CHALLENGE_ISSUED", "ALLOWED",
                command.ReasonCode, command.UserReference, actor.UserId, null, null, command.CorrelationId,
                _options.CentralPmsServiceIdentityId, now, cancellationToken);
        }
        else
        {
            await _authenticationRepository.RecordSecurityEventAsync("CREDENTIAL_RESET", "ALLOWED",
                command.ReasonCode, command.UserReference, actor.UserId, null, null, command.CorrelationId,
                _options.CentralPmsServiceIdentityId, now, cancellationToken);
        }
        OneTimeActivationMaterial? oneTime = null;
        OneTimeCredentialMaterial? oneTimeCredential = null;
        if (command.Purpose == "ACCOUNT_ACTIVATION" && command.DeliveryMode == ActivationDeliveryModes.AdminIssued)
        {
            var url = _challengeLinks.BuildUrl(command.Purpose, challenge.Reference, challenge.Secret);
            oneTime = new(challenge.Reference, challenge.Secret, expiresAt, url, url);
        }
        if (command.Purpose == "PASSWORD_RESET" && command.DeliveryMode == ActivationDeliveryModes.AdminIssued)
        {
            var url = _challengeLinks.BuildUrl(command.Purpose, challenge.Reference, challenge.Secret);
            oneTimeCredential = new(challenge.Reference, challenge.Secret, expiresAt, url, url);
        }
        return IdentityAdministrationResult<CredentialResetChallengeResult>.Succeeded(
            new(challenge.Reference, expiresAt, command.DeliveryMode,
                command.DeliveryMode == ActivationDeliveryModes.Email ? "EMAIL_SENT" : "ADMIN_ISSUED", oneTime,
                oneTimeCredential),
            command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<bool>> RevokeSessionsAsync(
        IdentityAdministrationActor actor,
        RevokeIdentitySessionCommand command,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (command.SessionReference.HasValue)
        {
            var session = await _authenticationRepository.FindSessionAsync(command.SessionReference.Value, cancellationToken);
            if (session is null || session.UserId != command.UserReference)
            {
                return Failed<bool>(IdentityAdministrationOutcome.NotFound, "IDENTITY_SESSION_NOT_FOUND", command.CorrelationId);
            }
            await _authenticationRepository.RevokeSessionAdministrativelyAsync(session.HumanSessionId,
                command.UserReference, actor.UserId, command.ReasonCode, command.CorrelationId,
                _options.CentralPmsServiceIdentityId, now, cancellationToken);
        }
        else
        {
            await _authenticationRepository.RevokeAllUserSessionsAdministrativelyAsync(command.UserReference,
                actor.UserId, command.ReasonCode, command.CorrelationId,
                _options.CentralPmsServiceIdentityId, now, cancellationToken);
        }
        return IdentityAdministrationResult<bool>.Succeeded(true, command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<IdentityMfaStatus>> ChangeMfaAsync(
        IdentityAdministrationActor actor,
        ChangeIdentityMfaCommand command,
        CancellationToken cancellationToken)
    {
        var changed = await _mfaAdministration.ChangeTotpAsync(command.UserReference, command.ExpectedRowVersion,
            command.Action, actor.UserId, command.ReasonCode, command.CorrelationId, cancellationToken);
        if (!changed)
        {
            return Failed<IdentityMfaStatus>(IdentityAdministrationOutcome.Conflict,
                "STALE_MFA_AUTHENTICATOR", command.CorrelationId);
        }

        return await _identityAdministration.GetMfaStatusAsync(
            actor, command.UserReference, command.CorrelationId, cancellationToken);
    }

    private static IdentityAdministrationResult<T> Failed<T>(
        IdentityAdministrationOutcome outcome,
        string classification,
        Guid correlationId) =>
        IdentityAdministrationResult<T>.Failed(outcome, classification,
            "The identity administration operation could not be completed.", correlationId);

    private static bool IsUsableEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try
        {
            var parsed = new MailAddress(value);
            return string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase) && parsed.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
