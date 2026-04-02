namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
/// Request payload for finalizing a payment attempt after verified payment outcome.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.4 Finalize Payment
///
/// Invariants Enforced:
/// - Finalization request must declare the canonical final attempt status
/// - Finalization must remain attributable to an authenticated internal actor
/// </summary>
/// <param name="FinalAttemptStatus">Canonical terminal payment-attempt status to apply.</param>
/// <param name="RequestedBy">Authenticated internal actor requesting finalization.</param>
public sealed record FinalizePaymentAttemptRequest(
    string FinalAttemptStatus,
    string RequestedBy);
