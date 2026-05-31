using System.Collections.Concurrent;
using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Process-local internal gate command lifecycle recorder for tests and local execution.
/// </summary>
public sealed class InMemoryGateCommandLifecycleRecorder : IGateCommandLifecycleRecorder
{
    private readonly ConcurrentDictionary<Guid, GateCommandLifecycleRecord> _commands = new();
    private readonly GateCommandRetryPolicy _retryPolicy = GateCommandRetryPolicy.Default;

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
            _retryPolicy.MaxAttempts,
            _retryPolicy.PolicyCode,
            RequestedAtUtc: now,
            LastAttemptedAtUtc: now,
            StartedAtUtc: now,
            CompletedAtUtc: null,
            NextAttemptAtUtc: null,
            TerminalFailureAtUtc: null,
            FailureCode: null,
            FailureReason: null,
            LastFailureCode: null,
            LastFailureReason: null,
            handoff.CorrelationId);

        var existing = _commands.GetOrAdd(processingKey, requested);
        if (ReferenceEquals(existing, requested))
        {
            return Task.FromResult(new GateCommandLifecycleStart(requested, Created: true, CanInvokeAdapter: true));
        }

        if (CanRetry(existing, now))
        {
            var retry = existing with
            {
                CommandStatus = GateCommandStatus.InProgress,
                AttemptCount = existing.AttemptCount + 1,
                LastAttemptedAtUtc = now,
                StartedAtUtc = now,
                CompletedAtUtc = null,
                NextAttemptAtUtc = null,
                FailureCode = null,
                FailureReason = null
            };
            _commands[processingKey] = retry;
            return Task.FromResult(new GateCommandLifecycleStart(retry, Created: false, CanInvokeAdapter: true));
        }

        if (existing.CommandStatus is GateCommandStatus.Retryable or GateCommandStatus.Failed
            && !_retryPolicy.HasAttemptsRemaining(existing.AttemptCount))
        {
            var terminal = existing with
            {
                CommandStatus = GateCommandStatus.TerminalFailure,
                TerminalFailureAtUtc = existing.TerminalFailureAtUtc ?? now,
                CompletedAtUtc = existing.CompletedAtUtc ?? now
            };
            _commands[processingKey] = terminal;
            return Task.FromResult(new GateCommandLifecycleStart(terminal, Created: false, CanInvokeAdapter: false));
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
            NextAttemptAtUtc = null,
            TerminalFailureAtUtc = null,
            FailureCode = null,
            FailureReason = null,
            LastFailureCode = null,
            LastFailureReason = null
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
        var completedAtUtc = DateTimeOffset.UtcNow;
        Update(commandId, command => command with
        {
            CommandStatus = ResolveFailureStatus(command, retryable),
            CompletedAtUtc = completedAtUtc,
            NextAttemptAtUtc = ResolveFailureStatus(command, retryable) == GateCommandStatus.Retryable
                ? completedAtUtc.Add(_retryPolicy.RetryDelay)
                : null,
            TerminalFailureAtUtc = ResolveFailureStatus(command, retryable) == GateCommandStatus.TerminalFailure
                ? completedAtUtc
                : null,
            FailureCode = failureCode,
            FailureReason = failureReason,
            LastFailureCode = failureCode,
            LastFailureReason = failureReason
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

    private bool CanRetry(GateCommandLifecycleRecord command, DateTimeOffset now) =>
        command.CommandStatus is GateCommandStatus.Retryable or GateCommandStatus.Failed
        && _retryPolicy.HasAttemptsRemaining(command.AttemptCount)
        && (!command.NextAttemptAtUtc.HasValue || command.NextAttemptAtUtc.Value <= now);

    private GateCommandStatus ResolveFailureStatus(GateCommandLifecycleRecord command, bool retryable)
    {
        if (!retryable)
        {
            return GateCommandStatus.Failed;
        }

        return _retryPolicy.HasAttemptsRemaining(command.AttemptCount)
            ? GateCommandStatus.Retryable
            : GateCommandStatus.TerminalFailure;
    }

    private static Guid ResolveProcessingKey(GateAuthorizationConsumedHandoff handoff) =>
        handoff.EventId == Guid.Empty ? handoff.GateAuthorizationConsumptionId : handoff.EventId;
}
