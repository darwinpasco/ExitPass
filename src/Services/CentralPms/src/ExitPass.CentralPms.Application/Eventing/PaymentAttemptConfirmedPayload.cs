namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the PaymentAttemptConfirmed integration event.
/// </summary>
public sealed class PaymentAttemptConfirmedPayload
{
    /// <summary>
    /// Gets the confirmed payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Gets the final attempt status.
    /// </summary>
    public string AttemptStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the provider reference that caused finality.
    /// </summary>
    public string ProviderReference { get; init; } = string.Empty;
}
