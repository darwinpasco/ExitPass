using System;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Represents persisted immutable evidence for an inbound provider webhook event.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Provider webhook evidence must be persisted outside core payment truth.
/// - Duplicate provider callbacks must be traceable by provider event identifier.
/// </summary>
/// <param name="ProviderWebhookEventRecordId">The internal webhook event record identifier.</param>
/// <param name="ProviderCode">The provider code.</param>
/// <param name="ProviderEventId">The provider event identifier.</param>
/// <param name="ProviderEventType">The provider event type.</param>
/// <param name="ProviderReference">The provider reference, when available.</param>
/// <param name="ProviderSessionId">The provider session identifier, when available.</param>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier.</param>
/// <param name="RawHeadersJson">The serialized raw inbound headers.</param>
/// <param name="RawBodyJson">The raw inbound body.</param>
/// <param name="IsAuthentic">Indicates whether authenticity verification passed.</param>
/// <param name="IsDuplicate">Indicates whether the event was identified as a duplicate.</param>
/// <param name="ReceivedAtUtc">The webhook receipt timestamp in UTC.</param>
public sealed record ProviderWebhookEventRecord(
    Guid ProviderWebhookEventRecordId,
    string ProviderCode,
    string ProviderEventId,
    string ProviderEventType,
    string ProviderReference,
    string ProviderSessionId,
    Guid PaymentAttemptId,
    string RawHeadersJson,
    string RawBodyJson,
    bool IsAuthentic,
    bool IsDuplicate,
    DateTimeOffset ReceivedAtUtc);
