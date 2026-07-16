using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Controlled recovery service for one stale IN_PROGRESS gate command.
/// </summary>
public sealed class GateCommandInProgressRecoveryService : IGateCommandInProgressRecoveryService
{
    private readonly IGateCommandInProgressRecoveryRepository _repository;
    private readonly ISystemClock _clock;

    /// <summary>
    /// Creates a stale gate command recovery service.
    /// </summary>
    public GateCommandInProgressRecoveryService(
        IGateCommandInProgressRecoveryRepository repository,
        ISystemClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<GateCommandRecoveryResult> RecoverAsync(
        Guid gateCommandId,
        DateTimeOffset staleBefore,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        if (gateCommandId == Guid.Empty)
        {
            return Rejected(gateCommandId, "GATE_COMMAND_ID_REQUIRED", "Gate command id is required.");
        }

        if (staleBefore == default)
        {
            return Rejected(gateCommandId, "STALE_BEFORE_REQUIRED", "Stale-before timestamp is required.");
        }

        if (retryDelay <= TimeSpan.Zero)
        {
            return Rejected(gateCommandId, "RETRY_DELAY_INVALID", "Retry delay must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await _repository.RecoverAsync(
            new GateCommandRecoveryRequest(
                gateCommandId,
                staleBefore,
                retryDelay,
                _clock.UtcNow),
            cancellationToken);
    }

    private static GateCommandRecoveryResult Rejected(
        Guid gateCommandId,
        string errorCode,
        string message) =>
        new(
            gateCommandId,
            GateCommandRecoveryOutcome.Rejected,
            CommandStatus: string.Empty,
            NextAttemptAt: null,
            TerminalFailureAt: null,
            Mutated: false,
            errorCode,
            message);
}
