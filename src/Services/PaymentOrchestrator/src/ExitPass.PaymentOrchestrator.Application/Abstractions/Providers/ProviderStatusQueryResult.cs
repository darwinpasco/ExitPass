using System;
using System.Collections.Generic;
using ExitPass.PaymentOrchestrator.Contracts.Payments;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Provider-neutral status-query evidence. This is not platform payment finality.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Provider evidence must be normalized before it can be considered for Central PMS reporting.
/// - Provider evidence does not create PaymentConfirmation or ExitAuthorization.
/// - Only verified terminal success may be marked eligible for later Central PMS reporting.
/// </summary>
/// <param name="ProviderCode">The payment provider code.</param>
/// <param name="ProviderProduct">The provider product code.</param>
/// <param name="ProviderSessionId">The provider session identifier.</param>
/// <param name="ProviderReference">The provider payment or transaction reference when available.</param>
/// <param name="SourceStatus">The source provider status when available.</param>
/// <param name="NormalizedStatus">The provider-neutral normalized status.</param>
/// <param name="IsTerminal">Indicates whether the provider evidence is terminal.</param>
/// <param name="IsSuccess">Indicates whether the provider evidence is successful.</param>
/// <param name="Retryable">Indicates whether status query may be retried safely.</param>
/// <param name="ReportableToCentralPms">Indicates whether the normalized evidence is eligible for future Central PMS reporting.</param>
/// <param name="AmountMinor">The provider amount in minor currency units, when available.</param>
/// <param name="CurrencyCode">The provider currency code, when available.</param>
/// <param name="ProviderObservedAtUtc">The provider-observed timestamp in UTC, when available.</param>
/// <param name="CorrelationId">The cross-service correlation identifier, when available.</param>
/// <param name="ErrorCode">A deterministic error code for rejected or failed query evidence.</param>
/// <param name="ErrorMessage">A safe diagnostic message for rejected or failed query evidence.</param>
/// <param name="Diagnostics">Safe normalized diagnostics. Must not contain secrets.</param>
public sealed record ProviderStatusQueryResult(
    string ProviderCode,
    string ProviderProduct,
    string ProviderSessionId,
    string? ProviderReference,
    string? SourceStatus,
    CanonicalPaymentOutcomeStatus NormalizedStatus,
    bool IsTerminal,
    bool IsSuccess,
    bool Retryable,
    bool ReportableToCentralPms,
    long? AmountMinor,
    string? CurrencyCode,
    DateTimeOffset? ProviderObservedAtUtc,
    Guid? CorrelationId,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyDictionary<string, string> Diagnostics);
