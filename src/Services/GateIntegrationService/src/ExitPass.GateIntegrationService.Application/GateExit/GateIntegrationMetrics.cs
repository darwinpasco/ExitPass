using System.Diagnostics.Metrics;

namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Emits Gate Integration Service business metrics for exit authorization consume/open attempts.
/// </summary>
public sealed class GateIntegrationMetrics : IDisposable
{
    /// <summary>
    /// Gets the canonical meter name for gate integration business telemetry.
    /// </summary>
    public const string MeterName = "ExitPass.GateIntegrationService.Business";

    private readonly Meter _meter;
    private readonly Counter<long> _consumeRequestsTotal;
    private readonly Counter<long> _consumeSuccessesTotal;
    private readonly Counter<long> _duplicateConsumeRejectionsTotal;
    private readonly Counter<long> _invalidAuthorizationConsumeAttemptsTotal;
    private readonly Histogram<double> _consumeLatencyMilliseconds;

    public GateIntegrationMetrics()
    {
        _meter = new Meter(MeterName);
        _consumeRequestsTotal = _meter.CreateCounter<long>(
            "exitpass_gate_consume_requests_total",
            "{request}",
            "Total gate consume requests handled by Gate Integration Service.");
        _consumeSuccessesTotal = _meter.CreateCounter<long>(
            "exitpass_gate_consume_successes_total",
            "{request}",
            "Total gate consume requests that opened the gate.");
        _duplicateConsumeRejectionsTotal = _meter.CreateCounter<long>(
            "exitpass_gate_duplicate_consume_rejections_total",
            "{request}",
            "Total duplicate gate consume requests rejected by Central PMS.");
        _invalidAuthorizationConsumeAttemptsTotal = _meter.CreateCounter<long>(
            "exitpass_gate_invalid_authorization_consume_attempts_total",
            "{request}",
            "Total invalid authorization consume attempts.");
        _consumeLatencyMilliseconds = _meter.CreateHistogram<double>(
            "exitpass_gate_consume_latency_ms",
            "ms",
            "Gate consume request latency in milliseconds.");
    }

    public void ConsumeRequested(string gateDeviceId)
    {
        _consumeRequestsTotal.Add(1, Tag("gate_device_id", gateDeviceId));
    }

    public void ConsumeSucceeded(string gateDeviceId)
    {
        _consumeSuccessesTotal.Add(1, Tag("gate_device_id", gateDeviceId));
    }

    public void DuplicateConsumeRejected(string gateDeviceId)
    {
        _duplicateConsumeRejectionsTotal.Add(1, Tag("gate_device_id", gateDeviceId));
    }

    public void InvalidAuthorizationConsumeAttempt(string gateDeviceId, string reason)
    {
        _invalidAuthorizationConsumeAttemptsTotal.Add(
            1,
            Tag("gate_device_id", gateDeviceId),
            Tag("reason", reason));
    }

    public void ConsumeLatency(string gateDeviceId, TimeSpan elapsed)
    {
        _consumeLatencyMilliseconds.Record(elapsed.TotalMilliseconds, Tag("gate_device_id", gateDeviceId));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private static KeyValuePair<string, object?> Tag(string key, string? value)
    {
        return new KeyValuePair<string, object?>(key, string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim().ToUpperInvariant());
    }
}
