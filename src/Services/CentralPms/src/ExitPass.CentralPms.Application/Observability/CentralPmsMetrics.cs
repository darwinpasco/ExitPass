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
    private readonly Counter<long> _exitAuthorizationsIssuedTotal;
    private readonly Counter<long> _exitAuthorizationConsumeOutcomesTotal;
    private readonly Counter<long> _exceptionsTotal;

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

        _exitAuthorizationsIssuedTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorizations_issued_total",
            unit: "{authorization}",
            description: "Total number of ExitAuthorizations issued by Central PMS.");

        _exitAuthorizationConsumeOutcomesTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorization_consume_outcomes_total",
            unit: "{authorization}",
            description: "Total number of ExitAuthorization consume outcomes observed by Central PMS.");

        _exceptionsTotal = _meter.CreateCounter<long>(
            name: "exitpass_exceptions_total",
            unit: "{exception}",
            description: "Total number of bounded application exceptions observed by Central PMS.");
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
    /// Records a successfully issued exit authorization.
    /// </summary>
    public void ExitAuthorizationIssued()
    {
        _exitAuthorizationsIssuedTotal.Add(1);
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
