namespace ExitPass.CentralPms.Contracts.Public.PaymentAttempts;

/// <summary>
/// Public API request for creating or reusing a payment attempt for a parking session.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
///
/// Invariants Enforced:
/// - Payment attempt creation is always tied to a specific parking session
/// - Payment attempt creation is always tied to a specific tariff snapshot
/// - Caller must declare the target payment provider
/// </summary>
public sealed class CreatePaymentAttemptRequest
{
    /// <summary>
    /// Canonical parking session identifier for which payment is being initiated.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Tariff snapshot identifier that fixes the pricing basis for the payment attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; set; }

    /// <summary>
    /// External payment provider code selected for the attempt, such as GCASH.
    /// </summary>
    public string PaymentProvider { get; set; } = string.Empty;
}
