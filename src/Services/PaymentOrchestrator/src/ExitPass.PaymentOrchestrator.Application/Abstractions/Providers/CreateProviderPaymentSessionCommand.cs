using System;
using System.Collections.Generic;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Represents the application-layer command for creating a provider payment session.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider session creation must remain bound to one canonical PaymentAttempt.
/// </summary>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier.</param>
/// <param name="AmountMinor">The payable amount in minor currency units.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="Description">The human-readable payment description.</param>
/// <param name="IdempotencyKey">The idempotency key for safe provider retries.</param>
/// <param name="SuccessUrl">The success return URL for the parker flow.</param>
/// <param name="FailureUrl">The failure return URL for the parker flow.</param>
/// <param name="CancelUrl">The cancel return URL for the parker flow.</param>
/// <param name="WebhookUrl">The callback URL that the provider should call.</param>
/// <param name="Metadata">Additional metadata used for provider correlation.</param>
public sealed record CreateProviderPaymentSessionCommand(
    Guid PaymentAttemptId,
    long AmountMinor,
    string Currency,
    string Description,
    string IdempotencyKey,
    string SuccessUrl,
    string FailureUrl,
    string CancelUrl,
    string WebhookUrl,
    IReadOnlyDictionary<string, string> Metadata);
