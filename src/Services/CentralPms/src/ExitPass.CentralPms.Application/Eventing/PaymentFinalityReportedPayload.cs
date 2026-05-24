namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the PaymentFinalityReportedToCentralPms integration event.
/// </summary>
public sealed class PaymentFinalityReportedPayload
{
    /// <summary>
    /// Gets the payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Gets the parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Gets the canonical payment confirmation identifier.
    /// </summary>
    public Guid PaymentConfirmationId { get; init; }

    /// <summary>
    /// Gets the final payment attempt status.
    /// </summary>
    public string AttemptStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the provider reference accepted as finality evidence.
    /// </summary>
    public string ProviderReference { get; init; } = string.Empty;
}
