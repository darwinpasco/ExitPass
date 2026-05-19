namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the PaymentAttemptCreated integration event.
/// </summary>
public sealed class PaymentAttemptCreatedPayload
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
    /// Tariff snapshot consumed by the payment attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Net payable amount in minor currency units.
    /// </summary>
    public long NetPayableMinorUnits { get; init; }

    /// <summary>
    /// Currency code for the payable amount.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// External payment provider code, when known.
    /// </summary>
    public string? ProviderCode { get; init; }

    /// <summary>
    /// Current payment attempt status.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
