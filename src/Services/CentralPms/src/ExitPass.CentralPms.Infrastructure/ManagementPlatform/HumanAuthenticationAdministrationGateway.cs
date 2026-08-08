using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.ManagementPlatform;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class HumanAuthenticationAdministrationGateway : IHumanAuthenticationAdministrationGateway
{
    private readonly IHumanAuthenticationRepository _authenticationRepository;
    private readonly IHumanMfaAdministrationService _mfaAdministration;
    private readonly ICredentialChallengeDelivery _challengeDelivery;
    private readonly IManagementPlatformIdentityAdministrationRepository _identityAdministration;
    private readonly HumanAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    public HumanAuthenticationAdministrationGateway(
        IHumanAuthenticationRepository authenticationRepository,
        IHumanMfaAdministrationService mfaAdministration,
        ICredentialChallengeDelivery challengeDelivery,
        IManagementPlatformIdentityAdministrationRepository identityAdministration,
        IOptions<HumanAuthenticationOptions> options,
        TimeProvider timeProvider)
    {
        _authenticationRepository = authenticationRepository;
        _mfaAdministration = mfaAdministration;
        _challengeDelivery = challengeDelivery;
        _identityAdministration = identityAdministration;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

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

        var now = _timeProvider.GetUtcNow();
        if (command.ExpiresAt <= now || command.ExpiresAt > now.AddMinutes(_options.CredentialChallengeMinutes))
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.Invalid,
                "INVALID_CREDENTIAL_CHALLENGE_EXPIRY", command.CorrelationId);
        }
        if (!_challengeDelivery.Enabled)
        {
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.IntegrationUnavailable,
                "CREDENTIAL_CHALLENGE_DELIVERY_NOT_CONFIGURED", command.CorrelationId);
        }

        var challenge = await _authenticationRepository.CreateCredentialChallengeAsync(
            command.UserReference, command.Purpose, now, command.ExpiresAt,
            _options.CentralPmsServiceIdentityId, command.CorrelationId, cancellationToken);
        try
        {
            await _challengeDelivery.DeliverAsync(new CredentialChallengeDeliveryRequest(
                command.UserReference, command.Purpose, challenge.Reference, challenge.Secret,
                command.ExpiresAt, command.CorrelationId), cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            await _authenticationRepository.RevokeCredentialChallengeAsync(challenge.Reference,
                _options.CentralPmsServiceIdentityId, "DELIVERY_FAILED", now, cancellationToken);
            return Failed<CredentialResetChallengeResult>(IdentityAdministrationOutcome.IntegrationUnavailable,
                "CREDENTIAL_CHALLENGE_DELIVERY_FAILED", command.CorrelationId);
        }

        if (command.Purpose == "ACCOUNT_ACTIVATION")
        {
            await _authenticationRepository.RecordSecurityEventAsync("ACTIVATION_CHALLENGE_ISSUED", "ALLOWED",
                command.ReasonCode, command.UserReference, actor.UserId, null, null, command.CorrelationId,
                _options.CentralPmsServiceIdentityId, now, cancellationToken);
        }
        return IdentityAdministrationResult<CredentialResetChallengeResult>.Succeeded(
            new(challenge.Reference, command.ExpiresAt), command.CorrelationId);
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
}
