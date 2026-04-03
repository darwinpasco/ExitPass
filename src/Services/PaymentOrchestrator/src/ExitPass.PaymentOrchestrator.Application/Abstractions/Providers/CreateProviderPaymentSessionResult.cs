using System;
using ExitPass.PaymentOrchestrator.Contracts.Payments;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Represents the result of creating a provider payment session.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider session evidence must be returned in a canonical structure.
/// </summary>
/// <param name="ProviderSessionId">The provider-specific session identifier.</param>
/// <param name="ProviderReference">The provider reference, when available.</param>
/// <param name="SessionStatus">The normalized provider session status.</param>
/// <param name="Handoff">The handoff instructions for the caller.</param>
/// <param name="ExpiresAtUtc">The provider session expiry timestamp, when available.</param>
/// <param name="RawResponseJson">The raw serialized provider response for evidence persistence.</param>
public sealed record CreateProviderPaymentSessionResult(
    string ProviderSessionId,
    string? ProviderReference,
    string SessionStatus,
    ProviderHandoffDto Handoff,
    DateTimeOffset? ExpiresAtUtc,
    string RawResponseJson);
