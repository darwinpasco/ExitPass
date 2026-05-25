namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Application service for internal event recovery operations.
/// </summary>
public interface IEventRecoveryService
{
    Task<IReadOnlyList<DeadLetterRecord>> ListDeadLettersAsync(
        ListDeadLettersQuery query,
        CancellationToken cancellationToken);

    Task<DeadLetterRecord> GetDeadLetterAsync(
        GetDeadLetterQuery query,
        CancellationToken cancellationToken);

    Task<DeadLetterRecord> RequestDeadLetterReplayAsync(
        RequestDeadLetterReplayCommand command,
        CancellationToken cancellationToken);

    Task<DeadLetterRecord> MarkDeadLetterReplayOutcomeAsync(
        MarkDeadLetterReplayOutcomeCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsumerCheckpointRecord>> ListConsumerCheckpointsAsync(
        ListConsumerCheckpointsQuery query,
        CancellationToken cancellationToken);

    Task<ConsumerCheckpointRecord> GetConsumerCheckpointAsync(
        GetConsumerCheckpointQuery query,
        CancellationToken cancellationToken);

    Task<ConsumerCheckpointRecord> UpdateConsumerCheckpointStatusAsync(
        UpdateConsumerCheckpointStatusCommand command,
        CancellationToken cancellationToken);
}
