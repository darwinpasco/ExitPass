using System.Collections.Concurrent;
using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Process-local internal gate command lifecycle recorder for tests and local execution.
/// </summary>
public sealed class InMemoryGateCommandLifecycleRecorder : IGateCommandLifecycleRecorder
{
    private readonly ConcurrentDictionary<Guid, GateCommandLifecycleRecord> _commands = new();

    /// <summary>
    /// Gets captured command lifecycle records.
    /// </summary>
    public IReadOnlyCollection<GateCommandLifecycleRecord> Commands => _commands.Values.ToArray();

    /// <inheritdoc />
    public Task<GateCommandLifecycleStart> BeginCommandAsync(
        GateAuthorizationConsumedHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        var processingKey = ResolveProcessingKey(handoff);
        var now = DateTimeOffset.UtcNow;
        var requested = new GateCommandLifecycleRecord(
            Guid.NewGuid(),
            processingKey,
            handoff.EventId,
            handoff.ExitAuthorizationId,
            handoff.GateAuthorizationConsumptionId,
            handoff.ParkingSessionId,
            handoff.PaymentAttemptId,
            handoff.TariffSnapshotId,
            handoff.GateDeviceId,
            handoff.GateDeviceIdentifier,
            handoff.LaneId,
            handoff.SiteId,
            handoff.VendorSystemId,
            GateCommandStatus.InProgress,
            AttemptCount: 1,
            RequestedAtUtc: now,
            StartedAtUtc: now,
            CompletedAtUtc: null,
            FailureCode: null,
            FailureReason: null,
            handoff.CorrelationId);

        var existing = _commands.GetOrAdd(processingKey, requested);
        if (ReferenceEquals(existing, requested))
        {
            return Task.FromResult(new GateCommandLifecycleStart(requested, Created: true, CanInvokeAdapter: true));
        }

        if (existing.CommandStatus == GateCommandStatus.Retryable || existing.CommandStatus == GateCommandStatus.Failed)
        {
            var retry = existing with
            {
                CommandStatus = GateCommandStatus.InProgress,
                AttemptCount = existing.AttemptCount + 1,
                StartedAtUtc = now,
                CompletedAtUtc = null,
                FailureCode = null,
                FailureReason = null
            };
            _commands[processingKey] = retry;
            return Task.FromResult(new GateCommandLifecycleStart(retry, Created: false, CanInvokeAdapter: true));
        }

        return Task.FromResult(new GateCommandLifecycleStart(existing, Created: false, CanInvokeAdapter: false));
    }

    /// <inheritdoc />
    public Task RecordSucceededAsync(
        Guid commandId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        Update(commandId, command => command with
        {
            CommandStatus = GateCommandStatus.Succeeded,
            CompletedAtUtc = completedAtUtc,
            FailureCode = null,
            FailureReason = null
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordFailedAsync(
        Guid commandId,
        string failureCode,
        string failureReason,
        bool retryable,
        CancellationToken cancellationToken)
    {
        Update(commandId, command => command with
        {
            CommandStatus = retryable ? GateCommandStatus.Retryable : GateCommandStatus.Failed,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            FailureCode = failureCode,
            FailureReason = failureReason
        });
        return Task.CompletedTask;
    }

    private void Update(Guid commandId, Func<GateCommandLifecycleRecord, GateCommandLifecycleRecord> update)
    {
        foreach (var entry in _commands)
        {
            if (entry.Value.CommandId == commandId)
            {
                _commands[entry.Key] = update(entry.Value);
                return;
            }
        }
    }

    private static Guid ResolveProcessingKey(GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;
}
