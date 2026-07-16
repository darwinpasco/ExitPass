namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Deterministic single-cycle coordinator for stale canonical gate command recovery.
/// </summary>
public sealed class GateCommandRecoveryCycleService : IGateCommandRecoveryCycleService
{
    private readonly IGateCommandRecoveryCandidateRepository _candidateRepository;
    private readonly IGateCommandInProgressRecoveryService _recoveryService;

    /// <summary>
    /// Creates a single-cycle gate command recovery coordinator.
    /// </summary>
    public GateCommandRecoveryCycleService(
        IGateCommandRecoveryCandidateRepository candidateRepository,
        IGateCommandInProgressRecoveryService recoveryService)
    {
        _candidateRepository = candidateRepository ?? throw new ArgumentNullException(nameof(candidateRepository));
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
    }

    /// <inheritdoc />
    public async Task<GateCommandRecoveryCycleResult> RunOnceAsync(
        DateTimeOffset staleBefore,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        if (staleBefore == default)
        {
            return NoWork("STALE_BEFORE_REQUIRED", "Stale-before timestamp is required.");
        }

        if (retryDelay <= TimeSpan.Zero)
        {
            return NoWork("RETRY_DELAY_INVALID", "Retry delay must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var candidate = await _candidateRepository.FindNextStaleAsync(staleBefore, cancellationToken);
        if (candidate is null)
        {
            return NoWork(null, "No stale IN_PROGRESS gate command was found.");
        }

        var recovery = await _recoveryService.RecoverAsync(
            candidate.GateCommandId,
            staleBefore,
            retryDelay,
            cancellationToken);

        return recovery.Outcome switch
        {
            GateCommandRecoveryOutcome.RecoveredRetryable => new GateCommandRecoveryCycleResult(
                candidate.GateCommandId,
                GateCommandRecoveryCycleOutcome.RecoveredRetryable,
                recovery.CommandStatus,
                recovery.NextAttemptAt,
                recovery.TerminalFailureAt,
                recovery.Mutated,
                ErrorCode: null,
                Message: null),
            GateCommandRecoveryOutcome.RecoveredTerminalFailure => new GateCommandRecoveryCycleResult(
                candidate.GateCommandId,
                GateCommandRecoveryCycleOutcome.RecoveredTerminalFailure,
                recovery.CommandStatus,
                recovery.NextAttemptAt,
                recovery.TerminalFailureAt,
                recovery.Mutated,
                ErrorCode: null,
                Message: null),
            _ => new GateCommandRecoveryCycleResult(
                candidate.GateCommandId,
                GateCommandRecoveryCycleOutcome.LostRaceOrIneligible,
                recovery.CommandStatus,
                recovery.NextAttemptAt,
                recovery.TerminalFailureAt,
                Mutated: false,
                recovery.ErrorCode ?? "GATE_COMMAND_RECOVERY_CANDIDATE_NOT_RECOVERED",
                recovery.Message ?? "Selected gate command was not recovered.")
        };
    }

    private static GateCommandRecoveryCycleResult NoWork(string? errorCode, string message) =>
        new(
            GateCommandId: null,
            GateCommandRecoveryCycleOutcome.NoWork,
            FinalCommandStatus: null,
            NextAttemptAt: null,
            TerminalFailureAt: null,
            Mutated: false,
            errorCode,
            message);
}
