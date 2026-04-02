namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
/// Response payload returned after provider confirmation evidence is recorded.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Recorded provider confirmation remains traceable and queryable
/// - Confirmation response must surface the canonical confirmation identifier and status
/// </summary>
/// <param name="PaymentConfirmationId">Canonical identifier of the recorded payment confirmation.</param>
/// <param name="PaymentAttemptId">Canonical payment attempt identifier bound to the confirmation.</param>
/// <param name="ProviderReference">Provider-side unique reference recorded for the confirmation.</param>
/// <param name="ProviderStatus">Provider-reported payment status that was recorded.</param>
/// <param name="ConfirmationStatus">Canonical confirmation status assigned by Central PMS.</param>
/// <param name="VerifiedTimestamp">Timestamp at which the confirmation was verified and recorded.</param>
public sealed record RecordPaymentConfirmationResponse(
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderStatus,
    string ConfirmationStatus,
    DateTimeOffset VerifiedTimestamp);
