namespace ExitPass.CentralPms.Application.PaymentAttempts.Results;

/// <summary>
/// Provider handoff information returned after Central PMS creates or reuses a payment attempt.
/// </summary>
public sealed class ProviderHandoffResult
{
    /// <summary>
    /// Handoff type, such as redirect, expected by the client.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Provider handoff URL when the selected rail requires navigation.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Expiration timestamp for the handoff, when one is available.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
