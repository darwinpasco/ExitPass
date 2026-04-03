using System;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// Represents the normalized PayMongo Checkout Session creation result returned by the raw PayMongo client.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Raw provider session creation results must be normalized before crossing into application logic.
/// </summary>
/// <param name="CheckoutSessionId">The PayMongo Checkout Session identifier.</param>
/// <param name="CheckoutUrl">The URL where the parker should be redirected.</param>
/// <param name="ExpiresAtUtc">The provider expiry timestamp, when available.</param>
/// <param name="RawJson">The raw provider response JSON.</param>
public sealed record PayMongoCheckoutSessionResponse(
    string CheckoutSessionId,
    string CheckoutUrl,
    DateTimeOffset? ExpiresAtUtc,
    string RawJson);
