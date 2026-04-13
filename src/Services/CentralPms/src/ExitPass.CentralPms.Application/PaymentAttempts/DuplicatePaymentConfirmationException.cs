namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Represents a deterministic duplicate-payment-confirmation rejection.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 7.3 Provider Callback / Confirmation Handling
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - The same provider reference must not be recorded more than once
/// - Duplicate provider evidence must be surfaced as a business conflict, not an internal server error
/// </summary>
public sealed class DuplicatePaymentConfirmationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatePaymentConfirmationException"/> class.
    /// </summary>
    /// <param name="message">Conflict message.</param>
    public DuplicatePaymentConfirmationException(string message)
        : base(message)
    {
    }
}
