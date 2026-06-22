using System;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Represents a provider status-query request for a known provider session.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Provider status queries must be scoped to a known provider session/reference.
/// - Expected amount and currency remain validation inputs, not client-side finality.
/// </summary>
/// <param name="ProviderSessionId">The provider session identifier to query.</param>
/// <param name="ProviderReference">Optional provider payment/transaction reference expected for the session.</param>
/// <param name="ExpectedAmountMinor">Optional expected amount in minor currency units.</param>
/// <param name="ExpectedCurrencyCode">Optional expected ISO currency code.</param>
/// <param name="CorrelationId">Optional cross-service correlation identifier.</param>
public sealed record ProviderStatusQueryCommand(
    string ProviderSessionId,
    string? ProviderReference,
    long? ExpectedAmountMinor,
    string? ExpectedCurrencyCode,
    Guid? CorrelationId);
