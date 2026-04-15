namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Indicates that a provider callback already exists and should be treated as a duplicate replay.
/// </summary>
public sealed class DuplicateProviderWebhookEventException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateProviderWebhookEventException"/> class.
    /// </summary>
    /// <param name="message">The exception message that describes the duplicate webhook event condition.</param>
    public DuplicateProviderWebhookEventException(string message)
        : base(message)
    {
    }
}
