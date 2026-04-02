namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.4 Finalize Payment
///
/// Invariants Enforced:
/// - Finalization result must expose the canonical post-finalization PaymentAttempt state
/// </summary>
public sealed record FinalizePaymentAttemptResult(
    Guid PaymentAttemptId,
    string AttemptStatus);
