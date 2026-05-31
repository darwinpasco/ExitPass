using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Default scope validator for handoff processing until a durable gate catalog repository is wired.
/// </summary>
public sealed class PassThroughGateAuthorizationConsumedScopeValidator
    : IGateAuthorizationConsumedScopeValidator
{
    /// <inheritdoc />
    public Task<GateAuthorizationConsumedScopeValidationResult> ValidateAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        return Task.FromResult(GateAuthorizationConsumedScopeValidationResult.Valid());
    }
}
