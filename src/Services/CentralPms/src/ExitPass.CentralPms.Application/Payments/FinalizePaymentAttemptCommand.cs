namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - Finalization must remain attributable
/// - Finalization must operate on a canonical PaymentAttempt identifier
/// </summary>
public sealed record FinalizePaymentAttemptCommand(
    Guid PaymentAttemptId,
    string FinalAttemptStatus,
    string RequestedBy,
    Guid CorrelationId);
