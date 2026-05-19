using System.Collections.Concurrent;
using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Process-local gate exit attempt recorder for v1.2 service behavior until durable reporting is introduced.
/// </summary>
public sealed class InMemoryGateExitAttemptRecorder : IGateExitAttemptRecorder
{
    private readonly ConcurrentQueue<GateExitAttemptRecord> _records = new();

    /// <summary>
    /// Gets the process-local gate exit attempt records captured by this recorder.
    /// </summary>
    public IReadOnlyCollection<GateExitAttemptRecord> Records => _records.ToArray();

    /// <inheritdoc />
    public Task RecordAsync(
        GateExitAttemptRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records.Enqueue(record);
        return Task.CompletedTask;
    }
}
