namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Indicates that a provider callback already exists and should be treated as a duplicate replay.
/// </summary>
public sealed class DuplicateProviderWebhookEventException : Exception
{
    public DuplicateProviderWebhookEventException(string message)
        : base(message)
    {
    }
}
