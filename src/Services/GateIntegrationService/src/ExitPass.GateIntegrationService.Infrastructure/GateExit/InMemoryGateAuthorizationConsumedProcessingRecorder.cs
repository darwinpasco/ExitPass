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
    public Task<GateAuthorizationConsumedProcessingRecord?> GetProcessedAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        _records.TryGetValue(eventId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task RecordProcessedAsync(
        GateAuthorizationConsumedProcessingRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records.TryAdd(record.EventId, record);
        return Task.CompletedTask;
    }
}
