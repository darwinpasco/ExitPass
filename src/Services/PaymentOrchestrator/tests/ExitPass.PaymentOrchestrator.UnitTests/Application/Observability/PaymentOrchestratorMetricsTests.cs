using System.Diagnostics.Metrics;
using ExitPass.PaymentOrchestrator.Application.Observability;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.Observability;

public sealed class PaymentOrchestratorMetricsTests
{
    [Fact]
    public void WebPayPaymentIntentCreated_EmitsExpectedMetricAndTags()
    {
        using var capture = new MetricCapture(PaymentOrchestratorMetrics.MeterName);
        using var metrics = new PaymentOrchestratorMetrics();

        metrics.WebPayPaymentIntentCreated("QRPH", "PAYMONGO");

        var measurement = capture.Measurements.First(
            x => x.Name == "exitpass_webpay_payment_intents_created_total" &&
                x.Tags.TryGetValue("payment_method", out var paymentMethod) &&
                paymentMethod == "QRPH" &&
                x.Tags.TryGetValue("provider", out var provider) &&
                provider == "PAYMONGO");
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public void ProviderWebhookDuplicateIgnored_EmitsExpectedMetricWithoutAub()
    {
        using var capture = new MetricCapture(PaymentOrchestratorMetrics.MeterName);
        using var metrics = new PaymentOrchestratorMetrics();

        metrics.ProviderWebhookDuplicateIgnored("PAYMONGO", "PAYMONGO_CHECKOUT_SESSION");

        var measurement = capture.Measurements.First(
            x => x.Name == "exitpass_provider_webhook_duplicates_ignored_total" &&
                x.Tags.TryGetValue("provider", out var provider) &&
                provider == "PAYMONGO");
        Assert.Equal("PAYMONGO", measurement.Tags["provider"]);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", measurement.Tags["provider_product"]);
        Assert.DoesNotContain(measurement.Tags, x => x.Value == "AUB");
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
