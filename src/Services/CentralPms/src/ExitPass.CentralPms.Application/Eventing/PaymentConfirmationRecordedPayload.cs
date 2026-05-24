namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the PaymentConfirmationRecorded integration event.
/// </summary>
public sealed class PaymentConfirmationRecordedPayload
{
    /// <summary>
    /// Gets the canonical payment confirmation identifier.
    /// </summary>
    public Guid PaymentConfirmationId { get; init; }

    /// <summary>
    /// Gets the confirmed payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Gets the provider reference used as confirmation evidence.
    /// </summary>
    public string ProviderReference { get; init; } = string.Empty;

    /// <summary>
    /// Gets the provider status accepted by Central PMS.
    /// </summary>
    public string ProviderStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp at which the provider evidence was verified.
    /// </summary>
    public DateTimeOffset VerifiedAtUtc { get; init; }
}
