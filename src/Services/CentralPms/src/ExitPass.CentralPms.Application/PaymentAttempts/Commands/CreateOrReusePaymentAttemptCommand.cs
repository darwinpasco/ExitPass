namespace ExitPass.CentralPms.Application.PaymentAttempts.Commands;

/// <summary>
/// Application command for creating or reusing a Central PMS payment attempt.
/// </summary>
public sealed class CreateOrReusePaymentAttemptCommand
{
    /// <summary>
    /// Parking session that anchors payment authority.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot that fixes the amount to collect.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Provider or rail code requested for the payment attempt.
    /// </summary>
    public string PaymentProviderCode { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key used to replay the same semantic request deterministically.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Correlation identifier for tracing the payment-to-exit chain.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Logical actor requesting payment attempt creation.
    /// </summary>
    public string RequestedBy { get; init; } = string.Empty;
}
