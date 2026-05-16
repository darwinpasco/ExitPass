namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Represents the normalized AUB payment initiation result returned by the raw AUB client.
/// </summary>
/// <param name="PaymentSessionId">The AUB payment session identifier.</param>
/// <param name="ProviderReference">The AUB payment reference.</param>
/// <param name="Status">The provider-specific session status.</param>
/// <param name="RedirectUrl">The provider handoff URL, when supplied by AUB.</param>
/// <param name="ExpiresAtUtc">The provider session expiry timestamp, when supplied by AUB.</param>
/// <param name="RawJson">The raw provider response JSON.</param>
public sealed record AubPaymentSessionResponse(
    string PaymentSessionId,
    string? ProviderReference,
    string Status,
    string? RedirectUrl,
    DateTimeOffset? ExpiresAtUtc,
    string RawJson);
