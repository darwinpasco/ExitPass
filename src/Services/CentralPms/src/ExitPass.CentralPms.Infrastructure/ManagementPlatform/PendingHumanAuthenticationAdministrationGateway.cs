using ExitPass.CentralPms.Application.ManagementPlatform;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

/// <summary>
/// Fail-closed I-020 integration boundary. I-020 replaces this registration with its authenticated
/// session, credential-challenge, and MFA administration primitives before I-021 can merge.
/// </summary>
public sealed class PendingHumanAuthenticationAdministrationGateway : IHumanAuthenticationAdministrationGateway
{
    private const string Classification = "HUMAN_AUTHENTICATION_RUNTIME_INTEGRATION_REQUIRED";
    private const string Message = "The human authentication administration runtime is not available.";

    public Task<IdentityAdministrationResult<CredentialResetChallengeResult>> IssueCredentialChallengeAsync(
        IdentityAdministrationActor actor,
        CreateCredentialResetChallengeCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(IdentityAdministrationResult<CredentialResetChallengeResult>.Failed(
            IdentityAdministrationOutcome.IntegrationUnavailable,
            Classification,
            Message,
            command.CorrelationId));

    public Task<IdentityAdministrationResult<bool>> RevokeSessionsAsync(
        IdentityAdministrationActor actor,
        RevokeIdentitySessionCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(IdentityAdministrationResult<bool>.Failed(
            IdentityAdministrationOutcome.IntegrationUnavailable,
            Classification,
            Message,
            command.CorrelationId));

    public Task<IdentityAdministrationResult<IdentityMfaStatus>> ChangeMfaAsync(
        IdentityAdministrationActor actor,
        ChangeIdentityMfaCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(IdentityAdministrationResult<IdentityMfaStatus>.Failed(
            IdentityAdministrationOutcome.IntegrationUnavailable,
            Classification,
            Message,
            command.CorrelationId));
}
