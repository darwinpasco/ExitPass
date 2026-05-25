namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Coordinates durable reconciliation outbox dispatch.
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
/// - Outbox publication is operational eventing only and never mutates payment, provider, exit, gate, or settlement truth.
/// - Reconciliation state changes remain database-authoritative and event publication is retryable evidence.
/// </summary>
public sealed class ReconciliationOutboxDispatcherService : IReconciliationOutboxDispatcherService
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    private readonly IReconciliationOutboxDispatcherRepository _repository;
    private readonly IReconciliationOutboxEventPublisher _publisher;

    /// <summary>
    /// Creates the dispatcher service.
    /// </summary>
    public ReconciliationOutboxDispatcherService(
        IReconciliationOutboxDispatcherRepository repository,
        IReconciliationOutboxEventPublisher publisher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    /// <inheritdoc />
    public async Task<ReconciliationOutboxDispatchResult> DispatchOnceAsync(
        DispatchReconciliationOutboxOnceCommand command,
        CancellationToken cancellationToken)
    {
        var brokerType = string.IsNullOrWhiteSpace(_publisher.BrokerType)
            ? "IN_PROCESS"
            : _publisher.BrokerType;
        var normalizedCommand = command with
        {
            Limit = NormalizeLimit(command.Limit),
            BrokerType = brokerType
        };
        var claimed = await _repository.ClaimPendingAsync(normalizedCommand, cancellationToken);

        var results = new List<ReconciliationOutboxDispatchItemResult>(claimed.Count);
        foreach (var outboxEvent in claimed)
        {
            var outcome = await _publisher.PublishAsync(outboxEvent, cancellationToken);
            if (outcome.Succeeded)
            {
                await _repository.MarkPublishedAsync(outboxEvent, outcome, cancellationToken);
                results.Add(new ReconciliationOutboxDispatchItemResult(
                    outboxEvent.OutboxEventId,
                    outboxEvent.EventPublicationId,
                    outboxEvent.EventType,
                    Succeeded: true,
                    PublicationStatus: "PUBLISHED",
                    FailureReasonCode: null,
                    outcome.BrokerMessageId));
            }
            else
            {
                results.Add(await _repository.MarkFailedAsync(outboxEvent, outcome, cancellationToken));
            }
        }

        return new ReconciliationOutboxDispatchResult(
            normalizedCommand.Limit,
            claimed.Count,
            results.Count(result => result.PublicationStatus == "PUBLISHED"),
            results.Count(result => !result.Succeeded),
            results.Count(result => result.PublicationStatus == "DEAD_LETTERED"),
            results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconciliationOutboxPendingRecord>> ListPendingAsync(
        ListPendingReconciliationOutboxQuery query,
        CancellationToken cancellationToken) =>
        _repository.ListPendingAsync(query with { Limit = NormalizeLimit(query.Limit) }, cancellationToken);

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit, MaxLimit);
    }
}
