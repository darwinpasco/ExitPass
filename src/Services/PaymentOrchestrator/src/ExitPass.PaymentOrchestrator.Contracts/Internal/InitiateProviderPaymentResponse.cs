using System;
using ExitPass.PaymentOrchestrator.Contracts.Payments;

namespace ExitPass.PaymentOrchestrator.Contracts.Internal;

/// <summary>
/// Internal response from Payment Orchestrator after provider payment session creation.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider session creation must remain traceable to one PaymentAttempt.
/// </summary>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier.</param>
/// <param name="ProviderCode">The provider code used for session creation.</param>
/// <param name="ProviderProduct">The provider product used for session creation.</param>
/// <param name="ProviderSessionId">The provider-specific session identifier.</param>
/// <param name="ProviderReference">The provider reference, when available.</param>
/// <param name="SessionStatus">The normalized provider session status.</param>
/// <param name="ProviderHandoff">The handoff instructions for the caller.</param>
/// <param name="ExpiresAtUtc">The provider session expiry timestamp, when available.</param>
public sealed record InitiateProviderPaymentResponse(
    Guid PaymentAttemptId,
    string ProviderCode,
    string ProviderProduct,
    string ProviderSessionId,
    string? ProviderReference,
    string SessionStatus,
    ProviderHandoffDto ProviderHandoff,
    DateTimeOffset? ExpiresAtUtc);
