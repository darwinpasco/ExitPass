using System.Diagnostics.Metrics;
using ExitPass.GateIntegrationService.Application.GateExit;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.GateIntegrationService.UnitTests.GateExit;

public sealed class GateIntegrationMetricsTests
{
    [Fact]
    public void DuplicateConsumeRejected_EmitsExpectedMetric()
    {
        using var capture = new MetricCapture(GateIntegrationMetrics.MeterName);
        using var metrics = new GateIntegrationMetrics();

        metrics.DuplicateConsumeRejected("exit-lane-01");

        var measurement = Assert.Single(capture.Measurements, x => x.Name == "exitpass_gate_duplicate_consume_rejections_total");
        Assert.Equal(1, measurement.Value);
        Assert.Equal("EXIT-LANE-01", measurement.Tags["gate_device_id"]);
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly object _sync = new();
        private readonly List<MetricMeasurement> _measurements = new();

        public MetricCapture(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(Capture);
            _listener.Start();
        }

        public IReadOnlyCollection<MetricMeasurement> Measurements
        {
            get
            {
                lock (_sync)
                {
                    return _measurements.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void Capture(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                values[tag.Key] = tag.Value?.ToString() ?? string.Empty;
            }

            lock (_sync)
            {
                _measurements.Add(new MetricMeasurement(instrument.Name, measurement, values));
            }
        }
    }

    private sealed record MetricMeasurement(
        string Name,
        long Value,
        IReadOnlyDictionary<string, string> Tags);
}

#pragma warning restore CS1591
