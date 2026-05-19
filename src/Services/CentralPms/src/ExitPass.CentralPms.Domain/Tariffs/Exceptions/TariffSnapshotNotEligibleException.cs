namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

/// <summary>
/// Indicates that a tariff snapshot cannot be bound to a new payment attempt.
/// </summary>
public sealed class TariffSnapshotNotEligibleException : Exception
{
    /// <summary>
    /// Creates the exception with the snapshot state that made it ineligible.
    /// </summary>
    public TariffSnapshotNotEligibleException(
        Guid tariffSnapshotId,
        TariffSnapshotStatus status,
        DateTimeOffset expiresAt,
        Guid? consumedByPaymentAttemptId)
        : base($"Tariff snapshot '{tariffSnapshotId}' is not eligible for payment.")
    {
        TariffSnapshotId = tariffSnapshotId;
        Status = status;
        ExpiresAt = expiresAt;
        ConsumedByPaymentAttemptId = consumedByPaymentAttemptId;
    }

    /// <summary>
    /// Tariff snapshot that could not be used for payment attempt creation.
    /// </summary>
    public Guid TariffSnapshotId { get; }

    /// <summary>
    /// Snapshot status observed during eligibility validation.
    /// </summary>
    public TariffSnapshotStatus Status { get; }

    /// <summary>
    /// Expiration timestamp used to reject stale payment quotes.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Existing payment attempt that already consumed the snapshot, when present.
    /// </summary>
    public Guid? ConsumedByPaymentAttemptId { get; }
}
