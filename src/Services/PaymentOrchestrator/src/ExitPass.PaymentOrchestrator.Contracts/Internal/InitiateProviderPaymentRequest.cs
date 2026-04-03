using System;
using System.Collections.Generic;

namespace ExitPass.PaymentOrchestrator.Contracts.Internal;

/// <summary>
/// Internal request from Central PMS to Payment Orchestrator to create a provider payment session.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - POA receives a payment intent derived from Central PMS authority.
/// - POA must not create independent financial truth.
/// </summary>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier from Central PMS.</param>
/// <param name="ProviderCode">The target provider code.</param>
/// <param name="ProviderProduct">The target provider product code.</param>
/// <param name="AmountMinor">The payable amount in minor currency units.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="Description">The human-readable payment description.</param>
/// <param name="IdempotencyKey">The idempotency key for safe retries.</param>
/// <param name="SuccessUrl">The success return URL for the parker flow.</param>
/// <param name="FailureUrl">The failure return URL for the parker flow.</param>
/// <param name="CancelUrl">The cancel return URL for the parker flow.</param>
/// <param name="WebhookUrl">The provider callback URL exposed by POA.</param>
/// <param name="Metadata">Additional metadata to correlate provider activity with ExitPass records.</param>
public sealed record InitiateProviderPaymentRequest(
    Guid PaymentAttemptId,
    string ProviderCode,
    string ProviderProduct,
    long AmountMinor,
    string Currency,
    string Description,
    string IdempotencyKey,
    string SuccessUrl,
    string FailureUrl,
    string CancelUrl,
    string WebhookUrl,
    IReadOnlyDictionary<string, string> Metadata);
