using System.Diagnostics.Metrics;
using ExitPass.CentralPms.Application.Observability;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.CentralPms.UnitTests.Application.Observability;

public sealed class CentralPmsMetricsTests
{
    [Fact]
    public void PaymentConfirmationRecorded_EmitsExpectedMetric()
    {
        using var capture = new MetricCapture(CentralPmsMetrics.MeterName);
        using var metrics = new CentralPmsMetrics();

        metrics.PaymentConfirmationRecorded("SUCCEEDED", "CONFIRMED");

        var measurement = Assert.Single(capture.Measurements, x => x.Name == "exitpass_payment_confirmations_recorded_total");
        Assert.Equal(1, measurement.Value);
        Assert.Equal("SUCCEEDED", measurement.Tags["provider_status"]);
        Assert.Equal("CONFIRMED", measurement.Tags["final_status"]);
    }

    [Fact]
    public void DurableEventPersistenceOutcome_WhenFailure_EmitsFailureMetric()
    {
        using var capture = new MetricCapture(CentralPmsMetrics.MeterName);
        using var metrics = new CentralPmsMetrics();

        metrics.DurableEventPersistenceOutcome("PaymentAttemptConfirmed", "FAILURE", "PostgresException");

        var measurement = Assert.Single(capture.Measurements, x => x.Name == "exitpass_durable_event_persistence_total");
        Assert.Equal("PAYMENTATTEMPTCONFIRMED", measurement.Tags["event_type"]);
        Assert.Equal("FAILURE", measurement.Tags["result"]);
        Assert.Equal("POSTGRESEXCEPTION", measurement.Tags["failure_reason"]);
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
