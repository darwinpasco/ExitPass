namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// Normalized PayMongo checkout-session status response.
/// </summary>
/// <param name="CheckoutSessionId">The PayMongo checkout session identifier.</param>
/// <param name="ProviderReference">The PayMongo payment/transaction reference when available.</param>
/// <param name="SourceStatus">The PayMongo source status.</param>
/// <param name="AmountMinor">The amount in minor currency units, when available.</param>
/// <param name="CurrencyCode">The ISO currency code, when available.</param>
/// <param name="ObservedAtUtc">The provider-observed timestamp in UTC, when available.</param>
/// <param name="RawJson">The raw PayMongo response JSON.</param>
public sealed record PayMongoCheckoutSessionStatusResponse(
    string CheckoutSessionId,
    string? ProviderReference,
    string? SourceStatus,
    long? AmountMinor,
    string? CurrencyCode,
    DateTimeOffset? ObservedAtUtc,
    string RawJson);
