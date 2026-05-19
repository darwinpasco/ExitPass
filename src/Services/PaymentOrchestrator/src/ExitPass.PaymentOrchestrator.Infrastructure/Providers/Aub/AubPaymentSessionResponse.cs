namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Represents the normalized AUB payment initiation result returned by the raw AUB client.
/// </summary>
/// <param name="OrderId">The merchant order identifier echoed by AUB.</param>
/// <param name="ResponseCode">The AUB response code.</param>
/// <param name="Message">The AUB response message.</param>
/// <param name="CashierUrl">The AUB cashier redirect URL, when supplied by AUB.</param>
/// <param name="ExpiresAtUtc">The provider session expiry timestamp, when supplied by AUB.</param>
/// <param name="RawJson">The raw provider response JSON.</param>
public sealed record AubPaymentSessionResponse(
    string OrderId,
    string ResponseCode,
    string Message,
    string? CashierUrl,
    DateTimeOffset? ExpiresAtUtc,
    string RawJson);
