using System.Diagnostics.Metrics;

namespace ExitPass.CentralPms.Application.Observability;

/// <summary>
/// Emits Central PMS business metrics aligned to the canonical ExitPass payment control chain.
/// </summary>
public sealed class CentralPmsMetrics : IDisposable
{
    /// <summary>
    /// Gets the canonical meter name for Central PMS business telemetry.
    /// </summary>
    public const string MeterName = "ExitPass.CentralPms.Business";

    private readonly Meter _meter;
    private readonly Counter<long> _paymentAttemptsCreatedTotal;
    private readonly Counter<long> _paymentAttemptsFinalizedTotal;
    private readonly Counter<long> _paymentAttemptFinalizeFailuresTotal;
    private readonly Counter<long> _verifiedPaymentOutcomesReceivedTotal;
    private readonly Counter<long> _paymentConfirmationsRecordedTotal;
    private readonly Counter<long> _exitAuthorizationsIssuedTotal;
    private readonly Counter<long> _exitAuthorizationIssuanceFailuresTotal;
    private readonly Counter<long> _exitAuthorizationFiscalGatingShadowEvaluationsTotal;
    private readonly Counter<long> _exitAuthorizationConsumeOutcomesTotal;
    private readonly Counter<long> _durableEventPersistenceTotal;
    private readonly Counter<long> _exceptionsTotal;
    private readonly Counter<long> _projectionAttemptsTotal;
    private readonly Counter<long> _projectionCompletionsTotal;
    private readonly Counter<long> _projectionFailuresTotal;
    private readonly Counter<long> _projectionLockContentionsTotal;
    private readonly Histogram<double> _projectionDurationMilliseconds;
    private readonly Histogram<long> _projectionRecords;

    /// <summary>
    /// Initializes a new instance of the <see cref="CentralPmsMetrics"/> class.
    /// </summary>
    public CentralPmsMetrics()
    {
        _meter = new Meter(MeterName);

        _paymentAttemptsCreatedTotal = _meter.CreateCounter<long>(
            name: "exitpass_payment_attempts_created_total",
            unit: "{attempt}",
            description: "Total number of PaymentAttempts created by Central PMS.");

        _paymentAttemptsFinalizedTotal = _meter.CreateCounter<long>(
            name: "exitpass_payment_attempts_finalized_total",
            unit: "{attempt}",
            description: "Total number of PaymentAttempts finalized by Central PMS.");

        _paymentAttemptFinalizeFailuresTotal = _meter.CreateCounter<long>(
            name: "exitpass_payment_attempt_finalize_failures_total",
            unit: "{attempt}",
            description: "Total number of PaymentAttempt finalization failures observed by Central PMS.");

        _verifiedPaymentOutcomesReceivedTotal = _meter.CreateCounter<long>(
            name: "exitpass_verified_payment_outcomes_received_total",
            unit: "{outcome}",
            description: "Total verified provider payment outcomes received by Central PMS.");

        _paymentConfirmationsRecordedTotal = _meter.CreateCounter<long>(
            name: "exitpass_payment_confirmations_recorded_total",
            unit: "{confirmation}",
            description: "Total authoritative payment confirmations recorded by Central PMS.");

        _exitAuthorizationsIssuedTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorizations_issued_total",
            unit: "{authorization}",
            description: "Total number of ExitAuthorizations issued by Central PMS.");

        _exitAuthorizationIssuanceFailuresTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorization_issuance_failures_total",
            unit: "{failure}",
            description: "Total ExitAuthorization issuance failures observed by Central PMS.");

        _exitAuthorizationFiscalGatingShadowEvaluationsTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorization_fiscal_gating_shadow_evaluations_total",
            unit: "{evaluation}",
            description: "Total non-enforcing fiscal gating shadow evaluations observed during ExitAuthorization issuance.");

        _exitAuthorizationConsumeOutcomesTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorization_consume_outcomes_total",
            unit: "{authorization}",
            description: "Total number of ExitAuthorization consume outcomes observed by Central PMS.");

        _durableEventPersistenceTotal = _meter.CreateCounter<long>(
            name: "exitpass_durable_event_persistence_total",
            unit: "{event}",
            description: "Total durable event persistence outcomes recorded by Central PMS.");

        _exceptionsTotal = _meter.CreateCounter<long>(
            name: "exitpass_exceptions_total",
            unit: "{exception}",
            description: "Total number of bounded application exceptions observed by Central PMS.");

        _projectionAttemptsTotal = _meter.CreateCounter<long>(
            "exitpass_vendor_session_projection_attempts_total",
            unit: "{attempt}",
            description: "Total target-scoped vendor projection attempts.");
        _projectionCompletionsTotal = _meter.CreateCounter<long>(
            "exitpass_vendor_session_projection_completions_total",
            unit: "{completion}",
            description: "Total successful vendor projection completions, including genuine zero-row results.");
        _projectionFailuresTotal = _meter.CreateCounter<long>(
            "exitpass_vendor_session_projection_failures_total",
            unit: "{failure}",
            description: "Total vendor projection failures by bounded classification.");
        _projectionLockContentionsTotal = _meter.CreateCounter<long>(
            "exitpass_vendor_session_projection_lock_contentions_total",
            unit: "{contention}",
            description: "Total target cycles deferred by distributed-lock contention.");
        _projectionDurationMilliseconds = _meter.CreateHistogram<double>(
            "exitpass_vendor_session_projection_duration_milliseconds",
            unit: "ms",
            description: "Duration of completed target projection operations.");
        _projectionRecords = _meter.CreateHistogram<long>(
            "exitpass_vendor_session_projection_records",
            unit: "{record}",
            description: "Records committed by successful target projection operations.");
    }

    /// <summary>
    /// Records a successfully created authoritative payment attempt.
    /// </summary>
    public void PaymentAttemptCreated(string provider)
    {
        _paymentAttemptsCreatedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", Normalize(provider)));
    }

    /// <summary>
    /// Records a successfully finalized authoritative payment attempt.
    /// </summary>
    public void PaymentAttemptFinalized(string finalStatus, string provider)
    {
        _paymentAttemptsFinalizedTotal.Add(
            1,
            new KeyValuePair<string, object?>("final_status", Normalize(finalStatus)),
            new KeyValuePair<string, object?>("provider", Normalize(provider)));
    }

    /// <summary>
    /// Records a payment-attempt finalization failure observed by Central PMS.
    /// </summary>
    public void PaymentAttemptFinalizeFailed(string failureReason, string provider)
    {
        _paymentAttemptFinalizeFailuresTotal.Add(
            1,
            new KeyValuePair<string, object?>("failure_reason", Normalize(failureReason)),
            new KeyValuePair<string, object?>("provider", Normalize(provider)));
    }

    /// <summary>
    /// Records a verified provider outcome received by Central PMS.
    /// </summary>
    public void VerifiedPaymentOutcomeReceived(string providerStatus, string finalStatus)
    {
        _verifiedPaymentOutcomesReceivedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider_status", Normalize(providerStatus)),
            new KeyValuePair<string, object?>("final_status", Normalize(finalStatus)));
    }

    /// <summary>
    /// Records an authoritative payment confirmation.
    /// </summary>
    public void PaymentConfirmationRecorded(string providerStatus, string finalStatus)
    {
        _paymentConfirmationsRecordedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider_status", Normalize(providerStatus)),
            new KeyValuePair<string, object?>("final_status", Normalize(finalStatus)));
    }

    /// <summary>
    /// Records a successfully issued exit authorization.
    /// </summary>
    public void ExitAuthorizationIssued()
    {
        _exitAuthorizationsIssuedTotal.Add(1);
    }

    /// <summary>
    /// Records an ExitAuthorization issuance failure.
    /// </summary>
    public void ExitAuthorizationIssuanceFailed(string failureReason)
    {
        _exitAuthorizationIssuanceFailuresTotal.Add(
            1,
            new KeyValuePair<string, object?>("failure_reason", Normalize(failureReason)));
    }

    /// <summary>
    /// Records a non-enforcing fiscal gating shadow evaluation observed during ExitAuthorization issuance.
    /// </summary>
    public void ExitAuthorizationFiscalGatingShadowEvaluated(string status, string blockedReason = "")
    {
        _exitAuthorizationFiscalGatingShadowEvaluationsTotal.Add(
            1,
            new KeyValuePair<string, object?>("status", Normalize(status)),
            new KeyValuePair<string, object?>("blocked_reason", Normalize(blockedReason)));
    }

    /// <summary>
    /// Records an exit-authorization consumption outcome.
    /// </summary>
    /// <param name="result">Bounded result such as CONSUMED, REJECTED, or FAILED.</param>
    /// <param name="reason">Bounded reason such as ALREADY_CONSUMED, EXPIRED, INVALID_REQUEST, or UNEXPECTED_FAILURE.</param>
    public void ExitAuthorizationConsumeOutcome(string result, string reason)
    {
        _exitAuthorizationConsumeOutcomesTotal.Add(
            1,
            new KeyValuePair<string, object?>("result", Normalize(result)),
            new KeyValuePair<string, object?>("reason", Normalize(reason)));
    }

    /// <summary>
    /// Records a bounded application exception for observability purposes.
    /// </summary>
    public void ExceptionObserved(string exceptionType, string operation)
    {
        _exceptionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("exception_type", Normalize(exceptionType)),
            new KeyValuePair<string, object?>("operation", Normalize(operation)));
    }

    /// <summary>
    /// Records the start of a target-scoped projection attempt.
    /// </summary>
    public void VendorSessionProjectionAttempted() => _projectionAttemptsTotal.Add(1);

    /// <summary>
    /// Records a committed projection completion.
    /// </summary>
    public void VendorSessionProjectionCompleted(long records, TimeSpan duration)
    {
        _projectionCompletionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("result", records == 0 ? "ZERO_ROWS" : "RECORDS_COMMITTED"));
        _projectionRecords.Record(records);
        _projectionDurationMilliseconds.Record(Math.Max(0, duration.TotalMilliseconds));
    }

    /// <summary>
    /// Records a bounded projection failure.
    /// </summary>
    public void VendorSessionProjectionFailed(string classification, bool retryable)
    {
        _projectionFailuresTotal.Add(
            1,
            new KeyValuePair<string, object?>("classification", Normalize(classification)),
            new KeyValuePair<string, object?>("retryable", retryable));
    }

    /// <summary>
    /// Records a target cycle deferred by lock contention.
    /// </summary>
    public void VendorSessionProjectionLockContended() => _projectionLockContentionsTotal.Add(1);

    /// <summary>
    /// Records durable event persistence success or failure.
    /// </summary>
    public void DurableEventPersistenceOutcome(string eventType, string result, string failureReason = "")
    {
        _durableEventPersistenceTotal.Add(
            1,
            new KeyValuePair<string, object?>("event_type", Normalize(eventType)),
            new KeyValuePair<string, object?>("result", Normalize(result)),
            new KeyValuePair<string, object?>("failure_reason", Normalize(failureReason)));
    }

    /// <summary>
    /// Disposes the underlying <see cref="Meter"/> owned by this metrics publisher.
    /// </summary>
    public void Dispose()
    {
        _meter.Dispose();
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim().ToUpperInvariant();
    }
}
