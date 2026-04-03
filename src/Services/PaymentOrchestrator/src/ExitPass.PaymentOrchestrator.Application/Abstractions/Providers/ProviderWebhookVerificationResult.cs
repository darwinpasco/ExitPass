using System;
using System.Collections.Generic;
using ExitPass.PaymentOrchestrator.Contracts.Payments;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Represents the result of verifying and normalizing a provider webhook.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only verified provider outcomes may be reported to Central PMS.
/// - Provider-specific states must be normalized before crossing the POA boundary.
/// </summary>
/// <param name="IsAuthentic">Indicates whether the webhook is authentic.</param>
/// <param name="EventId">The provider event identifier.</param>
/// <param name="EventType">The provider event type.</param>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier.</param>
/// <param name="ProviderReference">The provider reference.</param>
/// <param name="ProviderSessionId">The provider session identifier.</param>
/// <param name="CanonicalStatus">The canonical normalized payment outcome status.</param>
/// <param name="OccurredAtUtc">The provider event occurrence timestamp in UTC.</param>
/// <param name="AmountMinor">The payment amount in minor currency units.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="IsTerminal">Indicates whether the normalized outcome is terminal.</param>
/// <param name="IsSuccess">Indicates whether the normalized outcome is successful.</param>
/// <param name="RawAttributes">Additional raw normalized attributes for evidence and reporting.</param>
public sealed record ProviderWebhookVerificationResult(
    bool IsAuthentic,
    string EventId,
    string EventType,
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderSessionId,
    CanonicalPaymentOutcomeStatus CanonicalStatus,
    DateTimeOffset OccurredAtUtc,
    long AmountMinor,
    string Currency,
    bool IsTerminal,
    bool IsSuccess,
    IReadOnlyDictionary<string, string> RawAttributes);
