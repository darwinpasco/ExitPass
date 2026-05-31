using System.Collections.Concurrent;
using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Process-local consumed authorization handoff recorder for idempotent test and local behavior.
/// </summary>
public sealed class InMemoryGateAuthorizationConsumedProcessingRecorder
    : IGateAuthorizationConsumedProcessingRecorder
{
    private readonly ConcurrentDictionary<Guid, GateAuthorizationConsumedProcessingRecord> _records = new();

    /// <summary>
    /// Gets captured processing records.
    /// </summary>
    public IReadOnlyCollection<GateAuthorizationConsumedProcessingRecord> Records => _records.Values.ToArray();

    /// <inheritdoc />
    public Task<GateAuthorizationConsumedProcessingStart> BeginProcessingAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        var eventId = ResolveProcessingKey(handoff);
        var record = new GateAuthorizationConsumedProcessingRecord(
            eventId,
            handoff.ExitAuthorizationId,
            handoff.GateAuthorizationConsumptionId,
            handoff.TariffSnapshotId,
            "GATE_AUTHORIZATION_CONSUMED_PROCESSING",
            DateTimeOffset.UtcNow,
            GateAuthorizationConsumedProcessingStatus.Processing);

        var existing = _records.GetOrAdd(eventId, record);
        if (!ReferenceEquals(existing, record))
        {
            var alreadyProcessed = existing.ProcessingStatus == GateAuthorizationConsumedProcessingStatus.Processed;
            if (existing.ProcessingStatus == GateAuthorizationConsumedProcessingStatus.Failed)
            {
                var retry = existing with
                {
                    ProcessingStatus = GateAuthorizationConsumedProcessingStatus.Processing,
                    ResultCode = "GATE_AUTHORIZATION_CONSUMED_PROCESSING",
                    AttemptCount = existing.AttemptCount + 1,
                    LastFailureCode = null,
                    LastFailureReason = null
                };
                _records[eventId] = retry;
                return Task.FromResult(new GateAuthorizationConsumedProcessingStart(
                    retry,
                    CanInvokeAdapter: true,
                    AlreadyProcessed: false,
                    AlreadyInProgress: false));
            }

            return Task.FromResult(new GateAuthorizationConsumedProcessingStart(
                existing,
                CanInvokeAdapter: false,
                AlreadyProcessed: alreadyProcessed,
                AlreadyInProgress: !alreadyProcessed));
        }

        return Task.FromResult(new GateAuthorizationConsumedProcessingStart(
            record,
            CanInvokeAdapter: true,
            AlreadyProcessed: false,
            AlreadyInProgress: false));
    }

    /// <inheritdoc />
    public Task RecordProcessedAsync(
        GateAuthorizationConsumedProcessingRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.ProcessingKey] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordFailedAsync(
        GateAuthorizationConsumedHandoff handoff,
        string failureCode,
        string failureReason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        var eventId = ResolveProcessingKey(handoff);
        _records[eventId] = new GateAuthorizationConsumedProcessingRecord(
            eventId,
            handoff.ExitAuthorizationId,
            handoff.GateAuthorizationConsumptionId,
            handoff.TariffSnapshotId,
            failureCode,
            DateTimeOffset.UtcNow,
            GateAuthorizationConsumedProcessingStatus.Failed,
            AttemptCount: 1,
            LastFailureCode: failureCode,
            LastFailureReason: failureReason);
        return Task.CompletedTask;
    }

    private static Guid ResolveProcessingKey(GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;
}
