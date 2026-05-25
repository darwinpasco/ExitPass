namespace ExitPass.CentralPms.Contracts.Eventing;

/// <summary>
/// Request to dispatch one batch of pending reconciliation outbox events.
/// </summary>
public sealed record DispatchReconciliationOutboxOnceRequest(
    int? Limit,
    Guid? PublisherServiceIdentityId);

/// <summary>
/// Response for one dispatch batch.
/// </summary>
public sealed record DispatchReconciliationOutboxOnceResponse(
    int RequestedLimit,
    int ClaimedCount,
    int PublishedCount,
    int FailedCount,
    int DeadLetteredCount,
    IReadOnlyList<ReconciliationOutboxDispatchItemDto> Items);

/// <summary>
/// Dispatch result for one outbox event.
/// </summary>
public sealed record ReconciliationOutboxDispatchItemDto(
    Guid OutboxEventId,
    Guid EventPublicationId,
    string EventType,
    bool Succeeded,
    string PublicationStatus,
    string? FailureReasonCode,
    string? BrokerMessageId);

/// <summary>
/// Response for pending reconciliation outbox events.
/// </summary>
public sealed record PendingReconciliationOutboxEventsResponse(
    int Count,
    IReadOnlyList<PendingReconciliationOutboxEventDto> Items);

/// <summary>
/// Pending reconciliation outbox event summary.
/// </summary>
public sealed record PendingReconciliationOutboxEventDto(
    Guid OutboxEventId,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    string PublicationStatus,
    DateTimeOffset AvailableAt,
    DateTimeOffset? NextRetryAt,
    int RetryCount,
    int MaxRetryCount,
    Guid? CorrelationId,
    Guid? CausationId);
