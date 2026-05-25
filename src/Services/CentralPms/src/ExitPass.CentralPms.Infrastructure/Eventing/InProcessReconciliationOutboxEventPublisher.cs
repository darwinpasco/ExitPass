using ExitPass.CentralPms.Application.Eventing;

namespace ExitPass.CentralPms.Infrastructure.Eventing;

/// <summary>
/// Local publisher boundary for reconciliation outbox dispatch tests and development.
/// </summary>
public sealed class InProcessReconciliationOutboxEventPublisher : IReconciliationOutboxEventPublisher
{
    /// <inheritdoc />
    public Task<ReconciliationOutboxPublishOutcome> PublishAsync(
        ReconciliationOutboxEventRecord outboxEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ReconciliationOutboxPublishOutcome(
            Succeeded: true,
            BrokerMessageId: $"in-process-{outboxEvent.OutboxEventId:N}",
            FailureReasonCode: null,
            FailureDetailRef: null));
    }
}
