using System.Diagnostics.Metrics;

namespace ExitPass.PaymentOrchestrator.Application.Observability;

/// <summary>
/// Emits Payment Orchestrator business metrics for the WebPay provider-control chain.
/// </summary>
public sealed class PaymentOrchestratorMetrics : IDisposable
{
    /// <summary>
    /// Gets the canonical meter name for Payment Orchestrator business telemetry.
    /// </summary>
    public const string MeterName = "ExitPass.PaymentOrchestrator.Business";

    private readonly Meter _meter;
    private readonly Counter<long> _webPayPaymentIntentsCreatedTotal;
    private readonly Counter<long> _activePaymentAttemptConflictsTotal;
    private readonly Counter<long> _providerCheckoutSessionsCreatedTotal;
    private readonly Counter<long> _providerCheckoutCreationFailuresTotal;
    private readonly Counter<long> _providerWebhooksReceivedTotal;
    private readonly Counter<long> _providerWebhooksVerifiedTotal;
    private readonly Counter<long> _providerWebhookDuplicatesIgnoredTotal;
    private readonly Counter<long> _providerWebhookFinalizationFailuresTotal;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentOrchestratorMetrics"/> class.
    /// </summary>
    public PaymentOrchestratorMetrics()
    {
        _meter = new Meter(MeterName);

        _webPayPaymentIntentsCreatedTotal = _meter.CreateCounter<long>(
            "exitpass_webpay_payment_intents_created_total",
            "{intent}",
            "Total WebPay payment intents created by Payment Orchestrator.");

        _activePaymentAttemptConflictsTotal = _meter.CreateCounter<long>(
            "exitpass_webpay_active_payment_attempt_conflicts_total",
            "{conflict}",
            "Total active payment attempt conflicts observed while creating WebPay payment intents.");

        _providerCheckoutSessionsCreatedTotal = _meter.CreateCounter<long>(
            "exitpass_provider_checkout_sessions_created_total",
            "{session}",
            "Total hosted provider checkout sessions created.");

        _providerCheckoutCreationFailuresTotal = _meter.CreateCounter<long>(
            "exitpass_provider_checkout_creation_failures_total",
            "{failure}",
            "Total hosted provider checkout creation failures.");

        _providerWebhooksReceivedTotal = _meter.CreateCounter<long>(
            "exitpass_provider_webhooks_received_total",
            "{webhook}",
            "Total provider webhooks received by Payment Orchestrator.");

        _providerWebhooksVerifiedTotal = _meter.CreateCounter<long>(
            "exitpass_provider_webhooks_verified_total",
            "{webhook}",
            "Total authentic provider webhooks verified by Payment Orchestrator.");

        _providerWebhookDuplicatesIgnoredTotal = _meter.CreateCounter<long>(
            "exitpass_provider_webhook_duplicates_ignored_total",
            "{webhook}",
            "Total duplicate provider webhooks accepted without duplicate finality.");

        _providerWebhookFinalizationFailuresTotal = _meter.CreateCounter<long>(
            "exitpass_provider_webhook_finalization_failures_total",
            "{failure}",
            "Total failures while reporting terminal provider webhook finality.");
    }

    public void WebPayPaymentIntentCreated(string paymentMethod, string providerCode)
    {
        _webPayPaymentIntentsCreatedTotal.Add(
            1,
            Tag("payment_method", paymentMethod),
            Tag("provider", providerCode));
    }

    public void ActivePaymentAttemptConflict(string paymentMethod, string providerCode)
    {
        _activePaymentAttemptConflictsTotal.Add(
            1,
            Tag("payment_method", paymentMethod),
            Tag("provider", providerCode));
    }

    public void ProviderCheckoutSessionCreated(string providerCode, string providerProduct)
    {
        _providerCheckoutSessionsCreatedTotal.Add(
            1,
            Tag("provider", providerCode),
            Tag("provider_product", providerProduct));
    }

    public void ProviderCheckoutCreationFailed(string providerCode, string providerProduct, string failureReason)
    {
        _providerCheckoutCreationFailuresTotal.Add(
            1,
            Tag("provider", providerCode),
            Tag("provider_product", providerProduct),
            Tag("failure_reason", failureReason));
    }

    public void ProviderWebhookReceived(string providerCode, string providerProduct)
    {
        _providerWebhooksReceivedTotal.Add(
            1,
            Tag("provider", providerCode),
            Tag("provider_product", providerProduct));
    }

    public void ProviderWebhookVerified(string providerCode, string providerProduct, string eventType)
    {
        _providerWebhooksVerifiedTotal.Add(
            1,
            Tag("provider", providerCode),
            Tag("provider_product", providerProduct),
            Tag("event_type", eventType));
    }

    public void ProviderWebhookDuplicateIgnored(string providerCode, string providerProduct)
    {
        _providerWebhookDuplicatesIgnoredTotal.Add(
            1,
            Tag("provider", providerCode),
            Tag("provider_product", providerProduct));
    }

    public void ProviderWebhookFinalizationFailed(string providerCode, string providerProduct, string failureReason)
    {
        _providerWebhookFinalizationFailuresTotal.Add(
            1,
            Tag("provider", providerCode),
            Tag("provider_product", providerProduct),
            Tag("failure_reason", failureReason));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private static KeyValuePair<string, object?> Tag(string key, string? value)
    {
        return new KeyValuePair<string, object?>(key, Normalize(value));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim().ToUpperInvariant();
    }
}
