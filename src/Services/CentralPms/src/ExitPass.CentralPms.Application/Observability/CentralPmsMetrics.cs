// BRD requirement implemented:
// - 9.10 Payment Processing and Confirmation
// - 9.12 Exit Authorization
// - 9.16 Monitoring and Administration
// - 9.21 Audit and Traceability
//
// SDD sections:
// - 6.3 Initiate Payment Attempt
// - 6.4 Finalize Payment
// - 6.5 Issue Exit Authorization
// - 6.6 Consume Exit Authorization
// - 14 Observability
//
// System invariants enforced:
// - Telemetry must reflect canonical control-chain transitions
// - Telemetry must never mutate business state
// - Only Central PMS emits finality-related business telemetry for PaymentAttempt state
// - ExitAuthorization telemetry must reflect DB-backed issuance and consumption events

using System.Diagnostics.Metrics;

namespace ExitPass.CentralPms.Application.Observability;

/// <summary>
/// Emits Central PMS business metrics aligned to the canonical ExitPass payment control chain.
/// </summary>
/// <remarks>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.16 Monitoring and Administration
/// - 9.21 Audit and Traceability
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 6.6 Consume Exit Authorization
/// - 14 Observability
///
/// Invariants Enforced:
/// - Business telemetry is emitted from authoritative use-case boundaries
/// - Metrics do not redefine or weaken business truth
/// - Finality-related telemetry is emitted only after authoritative Central PMS actions
/// </remarks>
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
    private readonly Counter<long> _exitAuthorizationsConsumedTotal;
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

        _exitAuthorizationsConsumedTotal = _meter.CreateCounter<long>(
            name: "exitpass_exit_authorizations_consumed_total",
            unit: "{authorization}",
            description: "Total number of ExitAuthorizations consumed through Central PMS.");

        _exceptionsTotal = _meter.CreateCounter<long>(
            name: "exitpass_exceptions_total",
            unit: "{exception}",
            description: "Total number of bounded application exceptions observed by Central PMS.");
    }

    /// <summary>
    /// Records a successfully created authoritative payment attempt.
    /// </summary>
    /// <param name="provider">The normalized payment provider identifier associated with the attempt.</param>
    public void PaymentAttemptCreated(string provider)
    {
        _paymentAttemptsCreatedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", Normalize(provider)));
    }

    /// <summary>
    /// Records a successfully finalized authoritative payment attempt.
    /// </summary>
    /// <param name="finalStatus">The final attempt status produced by Central PMS.</param>
    /// <param name="provider">The normalized payment provider identifier associated with the attempt.</param>
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
    /// <param name="failureReason">The bounded failure reason classification.</param>
    /// <param name="provider">The normalized payment provider identifier associated with the attempt.</param>
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
    /// <param name="result">The bounded consume result, such as CONSUMED or REJECTED.</param>
    public void ExitAuthorizationConsumed(string result)
    {
        _exitAuthorizationsConsumedTotal.Add(
            1,
            new KeyValuePair<string, object?>("result", Normalize(result)));
    }

    /// <summary>
    /// Records a bounded application exception for observability purposes.
    /// </summary>
    /// <param name="exceptionType">The exception type name.</param>
    /// <param name="operation">The bounded operation name where the exception was observed.</param>
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

    /// <summary>
    /// Converts a metric label value into a bounded uppercase representation.
    /// </summary>
    /// <param name="value">The input value to normalize.</param>
    /// <returns>
    /// The trimmed uppercase value, or <c>UNKNOWN</c> when the input is null, empty, or whitespace.
    /// </returns>
    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim().ToUpperInvariant();
    }
}
