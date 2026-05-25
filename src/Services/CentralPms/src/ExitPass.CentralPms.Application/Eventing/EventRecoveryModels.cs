namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Query for dead-letter records.
/// </summary>
public sealed record ListDeadLettersQuery(int Limit, string? Status, string? ConsumerName);

/// <summary>
/// Query for one dead-letter record.
/// </summary>
public sealed record GetDeadLetterQuery(Guid DeadLetterRecordId);

/// <summary>
/// Command requesting replay of one dead-letter record.
/// </summary>
public sealed record RequestDeadLetterReplayCommand(
    Guid DeadLetterRecordId,
    Guid? RequestedByUserId,
    Guid? RequestedByServiceIdentityId,
    string? ReasonCode,
    Guid? CorrelationId);

/// <summary>
/// Command marking replay outcome for one dead-letter record.
/// </summary>
public sealed record MarkDeadLetterReplayOutcomeCommand(
    Guid DeadLetterRecordId,
    string OutcomeStatus,
    Guid? ResolvedByUserId,
    Guid? ResolvedByServiceIdentityId,
    string? ReasonCode,
    Guid? CorrelationId);

/// <summary>
/// Dead-letter record read model.
/// </summary>
public sealed record DeadLetterRecord(
    Guid DeadLetterRecordId,
    Guid? OutboxEventId,
    Guid? EventPublicationId,
    string? ConsumerName,
    string DeadLetterType,
    string DeadLetterStatus,
    string FailureReasonCode,
    string? FailureDetailRef,
    string? PayloadHash,
    DateTimeOffset DeadLetteredAt,
    DateTimeOffset? ReplayRequestedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionReasonCode,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Query for consumer checkpoint records.
/// </summary>
public sealed record ListConsumerCheckpointsQuery(int Limit, string? Status);

/// <summary>
/// Query for one consumer checkpoint by consumer name.
/// </summary>
public sealed record GetConsumerCheckpointQuery(string ConsumerName);

/// <summary>
/// Command for conservative consumer checkpoint status mutation.
/// </summary>
public sealed record UpdateConsumerCheckpointStatusCommand(
    string ConsumerName,
    string CheckpointStatus,
    Guid UpdatedByServiceIdentityId,
    string? FailureReasonCode,
    Guid? CorrelationId);

/// <summary>
/// Consumer checkpoint read model.
/// </summary>
public sealed record ConsumerCheckpointRecord(
    Guid ConsumerCheckpointId,
    string ConsumerName,
    string? ConsumerGroup,
    string? SubscriptionName,
    string? EventType,
    string? AggregateType,
    Guid? LastOutboxEventId,
    Guid? LastDomainEventId,
    string? LastBrokerOffset,
    string CheckpointStatus,
    long ProcessedCount,
    long FailureCount,
    DateTimeOffset? LastProcessedAt,
    DateTimeOffset? LastFailedAt,
    string? FailureReasonCode,
    DateTimeOffset? LockedAt,
    Guid? LockedByServiceIdentityId,
    Guid UpdatedByServiceIdentityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CorrelationId);
