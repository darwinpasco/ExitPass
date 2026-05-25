namespace ExitPass.CentralPms.Contracts.Eventing;

/// <summary>
/// Request to mark a dead-letter record for replay.
/// </summary>
public sealed record RequestDeadLetterReplayRequest(
    Guid? RequestedByUserId,
    Guid? RequestedByServiceIdentityId,
    string? ReasonCode);

/// <summary>
/// Request to mark the outcome of a previously requested dead-letter replay.
/// </summary>
public sealed record MarkDeadLetterReplayOutcomeRequest(
    string OutcomeStatus,
    Guid? ResolvedByUserId,
    Guid? ResolvedByServiceIdentityId,
    string? ReasonCode);

/// <summary>
/// Request to update a consumer checkpoint operational status.
/// </summary>
public sealed record UpdateConsumerCheckpointStatusRequest(
    string CheckpointStatus,
    Guid UpdatedByServiceIdentityId,
    string? FailureReasonCode);

/// <summary>
/// Response containing dead-letter records.
/// </summary>
public sealed record DeadLetterRecordsResponse(
    int Count,
    IReadOnlyList<DeadLetterRecordDto> Items);

/// <summary>
/// Response containing one dead-letter record.
/// </summary>
public sealed record DeadLetterRecordResponse(DeadLetterRecordDto Item);

/// <summary>
/// Response after a dead-letter replay request or outcome update.
/// </summary>
public sealed record DeadLetterReplayResponse(
    Guid DeadLetterRecordId,
    string DeadLetterStatus,
    DateTimeOffset? ReplayRequestedAt,
    DateTimeOffset? ResolvedAt,
    Guid? CorrelationId);

/// <summary>
/// Dead-letter record DTO.
/// </summary>
public sealed record DeadLetterRecordDto(
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
/// Response containing consumer checkpoint records.
/// </summary>
public sealed record ConsumerCheckpointsResponse(
    int Count,
    IReadOnlyList<ConsumerCheckpointDto> Items);

/// <summary>
/// Response containing one consumer checkpoint.
/// </summary>
public sealed record ConsumerCheckpointResponse(ConsumerCheckpointDto Item);

/// <summary>
/// Consumer checkpoint DTO.
/// </summary>
public sealed record ConsumerCheckpointDto(
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
