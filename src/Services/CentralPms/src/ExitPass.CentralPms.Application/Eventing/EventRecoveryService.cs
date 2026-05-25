namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Coordinates internal event recovery operations.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - Dead-letter replay is an event recovery workflow and never mutates payment, provider, exit, gate, or settlement truth.
/// - Consumer checkpoint operations are conservative operational controls over events-owned checkpoint rows only.
/// </summary>
public sealed class EventRecoveryService : IEventRecoveryService
{
    private static readonly string[] AllowedDeadLetterReplayRequestStatuses = ["OPEN", "UNDER_REVIEW"];
    private static readonly string[] TerminalDeadLetterStatuses = ["REPLAYED", "RESOLVED", "REJECTED", "CLOSED", "CANCELLED"];
    private static readonly string[] AllowedReplayOutcomeStatuses = ["REPLAYED", "RESOLVED", "REJECTED"];
    private static readonly string[] CheckpointStatuses = ["ACTIVE", "LOCKED", "FAILED", "PAUSED", "REPLAYING", "RESET", "RETIRED"];
    private static readonly string[] ConservativeCheckpointTargetStatuses = ["ACTIVE", "PAUSED", "FAILED", "RETIRED"];

    private readonly IEventRecoveryRepository _repository;

    public EventRecoveryService(IEventRecoveryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<IReadOnlyList<DeadLetterRecord>> ListDeadLettersAsync(
        ListDeadLettersQuery query,
        CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(query.Limit);
        return _repository.ListDeadLettersAsync(query with { Limit = limit }, cancellationToken);
    }

    public async Task<DeadLetterRecord> GetDeadLetterAsync(
        GetDeadLetterQuery query,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetDeadLetterAsync(query, cancellationToken);
        return record ?? throw new InvalidOperationException("DEAD_LETTER_RECORD_NOT_FOUND");
    }

    public async Task<DeadLetterRecord> RequestDeadLetterReplayAsync(
        RequestDeadLetterReplayCommand command,
        CancellationToken cancellationToken)
    {
        var current = await GetDeadLetterAsync(new GetDeadLetterQuery(command.DeadLetterRecordId), cancellationToken);
        if (TerminalDeadLetterStatuses.Contains(current.DeadLetterStatus, StringComparer.OrdinalIgnoreCase) ||
            string.Equals(current.DeadLetterStatus, "REPLAY_REQUESTED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DEAD_LETTER_REPLAY_NOT_ALLOWED");
        }

        if (!AllowedDeadLetterReplayRequestStatuses.Contains(current.DeadLetterStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DEAD_LETTER_REPLAY_NOT_ALLOWED");
        }

        var replayed = await _repository.RequestDeadLetterReplayAsync(command, cancellationToken);
        return replayed ?? throw new InvalidOperationException("DEAD_LETTER_RECORD_NOT_FOUND");
    }

    public async Task<DeadLetterRecord> MarkDeadLetterReplayOutcomeAsync(
        MarkDeadLetterReplayOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        var outcomeStatus = NormalizeRequiredStatus(command.OutcomeStatus, "OutcomeStatus");
        if (!AllowedReplayOutcomeStatuses.Contains(outcomeStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("INVALID_DEAD_LETTER_REPLAY_OUTCOME");
        }

        var current = await GetDeadLetterAsync(new GetDeadLetterQuery(command.DeadLetterRecordId), cancellationToken);
        if (!string.Equals(current.DeadLetterStatus, "REPLAY_REQUESTED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DEAD_LETTER_REPLAY_OUTCOME_NOT_ALLOWED");
        }

        var updated = await _repository.MarkDeadLetterReplayOutcomeAsync(
            command with { OutcomeStatus = outcomeStatus },
            cancellationToken);
        return updated ?? throw new InvalidOperationException("DEAD_LETTER_RECORD_NOT_FOUND");
    }

    public Task<IReadOnlyList<ConsumerCheckpointRecord>> ListConsumerCheckpointsAsync(
        ListConsumerCheckpointsQuery query,
        CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(query.Limit);
        return _repository.ListConsumerCheckpointsAsync(query with { Limit = limit }, cancellationToken);
    }

    public async Task<ConsumerCheckpointRecord> GetConsumerCheckpointAsync(
        GetConsumerCheckpointQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.ConsumerName))
        {
            throw new ArgumentException("ConsumerName is required.");
        }

        var matches = await _repository.GetConsumerCheckpointAsync(query, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException("CONSUMER_CHECKPOINT_NOT_FOUND"),
            1 => matches[0],
            _ => throw new InvalidOperationException("CONSUMER_CHECKPOINT_AMBIGUOUS")
        };
    }

    public async Task<ConsumerCheckpointRecord> UpdateConsumerCheckpointStatusAsync(
        UpdateConsumerCheckpointStatusCommand command,
        CancellationToken cancellationToken)
    {
        var targetStatus = NormalizeRequiredStatus(command.CheckpointStatus, nameof(command.CheckpointStatus));
        if (!CheckpointStatuses.Contains(targetStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("INVALID_CONSUMER_CHECKPOINT_STATUS");
        }

        if (!ConservativeCheckpointTargetStatuses.Contains(targetStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CONSUMER_CHECKPOINT_STATUS_UPDATE_NOT_ALLOWED");
        }

        var current = await GetConsumerCheckpointAsync(
            new GetConsumerCheckpointQuery(command.ConsumerName),
            cancellationToken);

        if (string.Equals(current.CheckpointStatus, "RETIRED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetStatus, "RETIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CONSUMER_CHECKPOINT_TERMINAL");
        }

        var updated = await _repository.UpdateConsumerCheckpointStatusAsync(
            command with { CheckpointStatus = targetStatus },
            cancellationToken);
        return updated ?? throw new InvalidOperationException("CONSUMER_CHECKPOINT_NOT_FOUND");
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, 100);

    private static string NormalizeRequiredStatus(string status, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException($"{parameterName} is required.");
        }

        return status.Trim().ToUpperInvariant();
    }
}
