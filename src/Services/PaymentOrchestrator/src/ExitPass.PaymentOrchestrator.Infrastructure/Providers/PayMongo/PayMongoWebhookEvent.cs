using System;
using System.Collections.Generic;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// Represents the normalized webhook event extracted from a PayMongo webhook request.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Raw provider webhook payloads must be normalized before canonical mapping.
/// </summary>
/// <param name="EventId">The provider event identifier.</param>
/// <param name="EventType">The provider event type.</param>
/// <param name="PaymentAttemptId">The ExitPass PaymentAttempt identifier extracted from metadata.</param>
/// <param name="ProviderReference">The provider payment reference.</param>
/// <param name="ProviderSessionId">The provider Checkout Session identifier, when available.</param>
/// <param name="OccurredAtUtc">The event occurrence timestamp in UTC.</param>
/// <param name="AmountMinor">The amount in minor units.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="RawAttributes">Additional normalized attributes.</param>
public sealed record PayMongoWebhookEvent(
    string EventId,
    string EventType,
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderSessionId,
    DateTimeOffset OccurredAtUtc,
    long AmountMinor,
    string Currency,
    IReadOnlyDictionary<string, string> RawAttributes);
