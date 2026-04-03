namespace ExitPass.PaymentOrchestrator.Contracts.Payments;

/// <summary>
/// Represents the canonical payment outcome states normalized by the Payment Orchestrator.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
///
/// SDD:
/// - 8.3 PaymentAttempt State Machine
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Provider-specific states must not leak into platform payment control logic.
/// </summary>
public enum CanonicalPaymentOutcomeStatus
{
    /// <summary>
    /// The provider interaction has started but no terminal outcome exists yet.
    /// </summary>
    PendingProvider = 1,

    /// <summary>
    /// The provider is waiting for customer action or completion of an intermediate step.
    /// </summary>
    AwaitingCustomerAction = 2,

    /// <summary>
    /// The provider outcome is successful.
    /// </summary>
    Succeeded = 3,

    /// <summary>
    /// The provider outcome is unsuccessful.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The provider session or payment opportunity expired.
    /// </summary>
    Expired = 5,

    /// <summary>
    /// The payment was cancelled before successful completion.
    /// </summary>
    Cancelled = 6
}
