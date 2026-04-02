namespace ExitPass.CentralPms.Contracts.Payments;

/// <summary>
/// Response payload returned after a payment attempt is finalized.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.4 Finalize Payment
///
/// Invariants Enforced:
/// - Finalization response must surface canonical PaymentAttempt state
/// - Exit authorization linkage, when issued, must remain explicit
/// </summary>
/// <param name="PaymentAttemptId">Canonical identifier of the finalized payment attempt.</param>
/// <param name="AttemptStatus">Canonical payment-attempt status after finalization.</param>
public sealed record FinalizePaymentAttemptResponse(
    Guid PaymentAttemptId,
    string AttemptStatus);
