namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Command for dispatching one batch of reconciliation outbox events.
/// </summary>
public sealed record DispatchReconciliationOutboxOnceCommand(
    int Limit,
    Guid? PublisherServiceIdentityId);

/// <summary>
/// Query for pending reconciliation outbox events.
/// </summary>
public sealed record ListPendingReconciliationOutboxQuery(int Limit);

/// <summary>
/// Claimed outbox event ready for publication.
/// </summary>
public sealed record ReconciliationOutboxEventRecord(
    Guid OutboxEventId,
    Guid? DomainEventId,
    Guid EventPublicationId,
    int PublicationAttemptNumber,
    string EventType,
    int EventVersion,
    string AggregateType,
    Guid AggregateId,
    string RoutingKey,
    string? ExchangeName,
    string? PayloadRef,
    string? PayloadHash,
    string PayloadContentType,
    Guid? CorrelationId,
    Guid? CausationId,
    int RetryCount,
    int MaxRetryCount);

/// <summary>
/// Pending outbox event summary.
/// </summary>
public sealed record ReconciliationOutboxPendingRecord(
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

/// <summary>
/// Publisher outcome for one outbox event.
/// </summary>
public sealed record ReconciliationOutboxPublishOutcome(
    bool Succeeded,
    string? BrokerMessageId,
    string? FailureReasonCode,
    string? FailureDetailRef);

/// <summary>
/// Dispatch result for one outbox event.
/// </summary>
public sealed record ReconciliationOutboxDispatchItemResult(
    Guid OutboxEventId,
    Guid EventPublicationId,
    string EventType,
    bool Succeeded,
    string PublicationStatus,
    string? FailureReasonCode,
    string? BrokerMessageId);

/// <summary>
/// Dispatch batch result.
/// </summary>
public sealed record ReconciliationOutboxDispatchResult(
    int RequestedLimit,
    int ClaimedCount,
    int PublishedCount,
    int FailedCount,
    int DeadLetteredCount,
    IReadOnlyList<ReconciliationOutboxDispatchItemResult> Items);
