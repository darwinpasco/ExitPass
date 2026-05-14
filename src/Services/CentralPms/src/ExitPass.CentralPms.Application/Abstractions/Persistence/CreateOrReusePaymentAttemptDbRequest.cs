namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Database routine request for creating or reusing a Central PMS payment attempt.
/// </summary>
public sealed class CreateOrReusePaymentAttemptDbRequest
{
    /// <summary>
    /// Parking session that anchors the payment attempt.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot to bind to the payment attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Provider or rail code requested for collection.
    /// </summary>
    public string PaymentProviderCode { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key used by the database routine to create or reuse deterministically.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Logical actor requesting the payment attempt.
    /// </summary>
    public string RequestedBy { get; init; } = string.Empty;

    /// <summary>
    /// Correlation identifier for the payment-to-exit control chain.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Timestamp supplied to the authoritative database routine.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; }
}
