namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
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
public sealed record FinalizePaymentAttemptRequest(
    string FinalAttemptStatus,
    string RequestedBy);
