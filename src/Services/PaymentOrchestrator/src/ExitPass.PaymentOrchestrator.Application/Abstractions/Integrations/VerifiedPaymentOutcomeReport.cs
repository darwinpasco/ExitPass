using System;
using System.Collections.Generic;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Represents the verified canonical payment outcome that POA reports to Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only verified provider outcomes may cross from POA into Central PMS.
/// - Provider-specific states must be normalized before entering Central PMS.
/// </summary>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier.</param>
/// <param name="ProviderCode">The provider code.</param>
/// <param name="ProviderReference">The provider reference.</param>
/// <param name="ProviderSessionId">The provider session identifier.</param>
/// <param name="CanonicalStatus">The canonical normalized payment outcome status.</param>
/// <param name="OccurredAtUtc">The provider event occurrence timestamp in UTC.</param>
/// <param name="AmountMinor">The payment amount in minor currency units.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="EventId">The provider event identifier.</param>
/// <param name="IsTerminal">Indicates whether the outcome is terminal.</param>
/// <param name="IsSuccess">Indicates whether the outcome is successful.</param>
/// <param name="RawAttributes">Additional normalized attributes for evidence and traceability.</param>
public sealed record VerifiedPaymentOutcomeReport(
    Guid PaymentAttemptId,
    string ProviderCode,
    string ProviderReference,
    string ProviderSessionId,
    string CanonicalStatus,
    DateTimeOffset OccurredAtUtc,
    long AmountMinor,
    string Currency,
    string EventId,
    bool IsTerminal,
    bool IsSuccess,
    IReadOnlyDictionary<string, string> RawAttributes);
