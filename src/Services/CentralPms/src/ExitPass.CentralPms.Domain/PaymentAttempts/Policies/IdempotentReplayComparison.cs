namespace ExitPass.CentralPms.Domain.PaymentAttempts.Policies;

/// <summary>
/// Compares a replayed payment-attempt request with the persisted attempt for idempotent reuse.
/// </summary>
public sealed class IdempotentReplayComparison
{
    /// <summary>
    /// Parking session requested by the replayed call.
    /// </summary>
    public Guid RequestedParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot requested by the replayed call.
    /// </summary>
    public Guid RequestedTariffSnapshotId { get; init; }

    /// <summary>
    /// Provider code requested by the replayed call.
    /// </summary>
    public string RequestedProviderCode { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key stored with the original payment attempt.
    /// </summary>
    public string PersistedIdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Parking session stored with the original payment attempt.
    /// </summary>
    public Guid PersistedParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot stored with the original payment attempt.
    /// </summary>
    public Guid PersistedTariffSnapshotId { get; init; }

    /// <summary>
    /// Provider code stored with the original payment attempt.
    /// </summary>
    public string PersistedProviderCode { get; init; } = string.Empty;
}
