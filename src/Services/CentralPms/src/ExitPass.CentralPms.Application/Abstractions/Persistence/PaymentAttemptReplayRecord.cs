namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Persisted payment attempt fields needed to validate idempotent replay before tariff eligibility rejection.
/// </summary>
public sealed class PaymentAttemptReplayRecord
{
    /// <summary>
    /// Existing payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Parking session bound to the existing attempt.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot bound to the existing attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Idempotency key stored with the existing attempt.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Payment rail code stored through the attempt payment rail.
    /// </summary>
    public string? RailCode { get; init; }

    /// <summary>
    /// Payment provider code stored through the attempt payment rail.
    /// </summary>
    public string? ProviderCode { get; init; }

    /// <summary>
    /// Amount persisted on the existing attempt.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Currency code persisted on the existing attempt.
    /// </summary>
    public string CurrencyCode { get; init; } = string.Empty;
}
