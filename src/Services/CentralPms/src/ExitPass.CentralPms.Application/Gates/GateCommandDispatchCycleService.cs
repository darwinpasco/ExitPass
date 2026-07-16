using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Deterministic single-cycle dispatcher for canonical gate commands.
/// </summary>
public sealed class GateCommandDispatchCycleService : IGateCommandDispatchCycleService
{
    private const string RequestedStatus = "REQUESTED";
    private const string RetryableStatus = "RETRYABLE";

    private readonly IGateCommandDispatchCandidateRepository _candidateRepository;
    private readonly IGateCommandExecutionService _executionService;
    private readonly ISystemClock _clock;

    /// <summary>
    /// Creates a single-cycle gate command dispatcher.
    /// </summary>
    public GateCommandDispatchCycleService(
        IGateCommandDispatchCandidateRepository candidateRepository,
        IGateCommandExecutionService executionService,
        ISystemClock clock)
    {
        _candidateRepository = candidateRepository ?? throw new ArgumentNullException(nameof(candidateRepository));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<GateCommandDispatchCycleResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidate = await _candidateRepository.FindNextEligibleAsync(_clock.UtcNow, cancellationToken);
        if (candidate is null)
        {
            return new GateCommandDispatchCycleResult(
                GateCommandId: null,
                GateCommandDispatchCycleOutcome.NoWork,
                CandidateStatus: null,
                FinalCommandStatus: null,
                HikCentralGateActionAuditId: null,
                AdapterInvoked: false,
                ErrorCode: null,
                Message: "No eligible gate command was found.");
        }

        var execution = candidate.CommandStatus switch
        {
            RequestedStatus => await _executionService.ExecuteAsync(candidate.GateCommandId, cancellationToken),
            RetryableStatus => await _executionService.RetryAsync(candidate.GateCommandId, cancellationToken),
            _ => new GateCommandExecutionResult(
                candidate.GateCommandId,
                GateCommandExecutionOutcome.Rejected,
                candidate.CommandStatus,
                HikCentralGateActionAuditId: null,
                AdapterInvoked: false,
                "GATE_COMMAND_DISPATCH_CANDIDATE_STATUS_UNSUPPORTED",
                "Gate command candidate status is not supported by the dispatcher.")
        };

        if (execution.Outcome == GateCommandExecutionOutcome.Executed)
        {
            return new GateCommandDispatchCycleResult(
                candidate.GateCommandId,
                GateCommandDispatchCycleOutcome.Dispatched,
                candidate.CommandStatus,
                execution.CommandStatus,
                execution.HikCentralGateActionAuditId,
                execution.AdapterInvoked,
                ErrorCode: null,
                Message: null);
        }

        return new GateCommandDispatchCycleResult(
            candidate.GateCommandId,
            GateCommandDispatchCycleOutcome.LostRaceOrIneligible,
            candidate.CommandStatus,
            execution.CommandStatus,
            execution.HikCentralGateActionAuditId,
            execution.AdapterInvoked,
            execution.ErrorCode ?? "GATE_COMMAND_DISPATCH_CANDIDATE_NOT_EXECUTED",
            execution.Message ?? "Selected gate command was not executed.");
    }
}
