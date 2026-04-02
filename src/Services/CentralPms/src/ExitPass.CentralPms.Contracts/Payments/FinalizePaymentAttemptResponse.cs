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
/// - Finalization response must surface canonical PaymentAttempt state
/// - Exit authorization linkage, when issued, must remain explicit
/// </summary>
public sealed record FinalizePaymentAttemptResponse(
    Guid PaymentAttemptId,
    string AttemptStatus);
