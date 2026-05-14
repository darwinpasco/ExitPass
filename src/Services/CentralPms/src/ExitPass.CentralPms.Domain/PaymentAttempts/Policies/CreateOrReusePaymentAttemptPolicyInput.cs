namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

/// <summary>
/// Domain policy input for creating or reusing a Central PMS payment attempt.
/// </summary>
public sealed class CreateOrReusePaymentAttemptPolicyInput
{
    /// <summary>
    /// Parking session that anchors payment authority for the attempt.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot that fixes the payable amount to bind to the attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Provider or rail requested for collecting the payment.
    /// </summary>
    public PaymentProvider PaymentProvider { get; init; } = PaymentProvider.GCash;

    /// <summary>
    /// Client supplied key used for deterministic create-or-reuse behavior.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;
}
