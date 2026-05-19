namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the PaymentAttemptReused integration event.
/// </summary>
public sealed class PaymentAttemptReusedPayload
{
    /// <summary>
    /// Canonical payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Parking session bound to the payment attempt.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Tariff snapshot bound to the reused payment attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Current payment attempt status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Deterministic reason the payment attempt was reused.
    /// </summary>
    public string ReuseReason { get; init; } = string.Empty;
}
