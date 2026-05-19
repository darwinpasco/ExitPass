using ExitPass.PaymentOrchestrator.Contracts.Payments;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Represents the normalized webhook event extracted from an AUB provider callback.
/// </summary>
/// <param name="EventId">The AUB event identifier.</param>
/// <param name="EventType">The AUB event type.</param>
/// <param name="PaymentAttemptId">The ExitPass payment attempt identifier.</param>
/// <param name="ProviderReference">The AUB payment reference.</param>
/// <param name="ProviderSessionId">The AUB payment session identifier.</param>
/// <param name="CanonicalStatus">The canonical provider-neutral outcome status.</param>
/// <param name="OccurredAtUtc">The event occurrence timestamp.</param>
/// <param name="AmountMinor">The amount in minor currency units.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="RawAttributes">Provider attributes retained as evidence.</param>
public sealed record AubWebhookEvent(
    string EventId,
    string EventType,
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderSessionId,
    CanonicalPaymentOutcomeStatus CanonicalStatus,
    DateTimeOffset OccurredAtUtc,
    long AmountMinor,
    string Currency,
    IReadOnlyDictionary<string, string> RawAttributes);
