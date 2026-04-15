namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Canonical verified payment outcome reported by POA to Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - POA reports only verified outcomes.
/// - POA does not finalize payment state itself.
/// - The report must carry the authoritative identifiers required by Central PMS.
/// </summary>
/// <param name="PaymentAttemptId">Canonical payment attempt identifier.</param>
/// <param name="ParkingSessionId">Canonical parking session identifier required by Central PMS.</param>
/// <param name="RequestedByUserId">Calling actor identity required by Central PMS.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
/// <param name="ProviderCode">Normalized provider code.</param>
/// <param name="ProviderReference">Provider-side reference.</param>
/// <param name="ProviderSessionId">Provider-side session identifier.</param>
/// <param name="CanonicalStatus">Canonical payment outcome status.</param>
/// <param name="OccurredAtUtc">Verified provider event timestamp in UTC.</param>
/// <param name="AmountMinor">Verified amount in minor units.</param>
/// <param name="Currency">Verified currency code.</param>
/// <param name="EventId">Provider event identifier used as idempotency key.</param>
/// <param name="IsTerminal">Whether the verified outcome is terminal.</param>
/// <param name="IsSuccess">Whether the verified outcome is successful.</param>
/// <param name="RawAttributes">Additional provider attributes kept for audit and diagnostics.</param>
public sealed record VerifiedPaymentOutcomeReport(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid RequestedByUserId,
    Guid CorrelationId,
    string ProviderCode,
    string? ProviderReference,
    string ProviderSessionId,
    string CanonicalStatus,
    DateTimeOffset OccurredAtUtc,
    long AmountMinor,
    string Currency,
    string EventId,
    bool IsTerminal,
    bool IsSuccess,
    IReadOnlyDictionary<string, string>? RawAttributes);
