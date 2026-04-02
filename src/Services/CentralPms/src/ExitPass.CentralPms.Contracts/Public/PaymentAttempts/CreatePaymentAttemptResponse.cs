namespace ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

/// <summary>
/// Public API response returned after a payment attempt is created or reused.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
///
/// Invariants Enforced:
/// - Response always identifies the canonical payment attempt
/// - Response always returns the current attempt status
/// - Response indicates whether the result was newly created or safely reused
/// </summary>
public sealed class CreatePaymentAttemptResponse
{
    /// <summary>
    /// Canonical identifier of the payment attempt created or reused by Central PMS.
    /// </summary>
    public Guid PaymentAttemptId { get; set; }

    /// <summary>
    /// Current lifecycle status of the payment attempt.
    /// </summary>
    public string AttemptStatus { get; set; } = string.Empty;

    /// <summary>
    /// External payment provider code bound to the payment attempt.
    /// </summary>
    public string PaymentProvider { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether Central PMS reused an existing idempotent attempt instead of creating a new one.
    /// </summary>
    public bool WasReused { get; set; }

    /// <summary>
    /// Provider handoff instructions or redirect metadata for continuing the payment flow.
    /// </summary>
    public ProviderHandoffDto ProviderHandoff { get; set; } = new();
}
