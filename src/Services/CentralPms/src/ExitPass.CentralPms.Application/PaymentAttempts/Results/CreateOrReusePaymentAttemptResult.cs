namespace ExitPass.CentralPms.Application.PaymentAttempts.Results;

/// <summary>
/// Application result for a Central PMS payment attempt that was created or reused.
/// </summary>
public sealed class CreateOrReusePaymentAttemptResult
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
    /// Tariff snapshot bound to the payment attempt.
    /// </summary>
    public Guid TariffSnapshotId { get; init; }

    /// <summary>
    /// Current Central PMS payment attempt status.
    /// </summary>
    public string AttemptStatus { get; init; } = string.Empty;

    /// <summary>
    /// Provider or rail code selected for collection.
    /// </summary>
    public string PaymentProviderCode { get; init; } = string.Empty;

    /// <summary>
    /// Handoff information for the selected provider or rail.
    /// </summary>
    public ProviderHandoffResult ProviderHandoff { get; init; } = new();

    /// <summary>
    /// Indicates whether this response came from an idempotent replay.
    /// </summary>
    public bool WasReused { get; init; }
}
