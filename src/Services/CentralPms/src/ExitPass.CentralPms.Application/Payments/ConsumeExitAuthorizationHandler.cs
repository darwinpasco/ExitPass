using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Consumes a single-use exit authorization through the canonical DB-backed control path.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - ExitAuthorization may only be consumed once
/// - Consumption remains DB-backed and deterministic
/// - Consumption requests are fully traceable through correlation metadata
/// </summary>
public sealed class ConsumeExitAuthorizationHandler : IConsumeExitAuthorizationUseCase
{
    private readonly IConsumeExitAuthorizationGateway _gateway;
    private readonly ISystemClock _systemClock;

    public ConsumeExitAuthorizationHandler(
        IConsumeExitAuthorizationGateway gateway,
        ISystemClock systemClock)
    {
        _gateway = gateway;
        _systemClock = systemClock;
    }

    public async Task<ConsumeExitAuthorizationResult> ExecuteAsync(
        ConsumeExitAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ExitAuthorizationId == Guid.Empty)
        {
            throw new ArgumentException("ExitAuthorizationId is required.", nameof(command));
        }

        if (command.RequestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByUserId is required.", nameof(command));
        }

        if (command.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId is required.", nameof(command));
        }

        var dbResult = await _gateway.ConsumeAsync(
            new ConsumeExitAuthorizationDbRequest
            {
                ExitAuthorizationId = command.ExitAuthorizationId,
                RequestedByUserId = command.RequestedByUserId,
                CorrelationId = command.CorrelationId,
                RequestedAt = _systemClock.UtcNow
            },
            cancellationToken);

        return new ConsumeExitAuthorizationResult(
            ExitAuthorizationId: dbResult.ExitAuthorizationId,
            AuthorizationStatus: dbResult.AuthorizationStatus,
            ConsumedAt: dbResult.ConsumedAt);
    }
}
