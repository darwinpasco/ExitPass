using System;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Represents persisted provider session evidence owned by the Payment Orchestrator.
///
/// BRD:
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 9.2 Payments Domain
///
/// Invariants Enforced:
/// - Provider session evidence must remain traceable to one PaymentAttempt.
/// </summary>
/// <param name="ProviderSessionRecordId">The internal provider session record identifier.</param>
/// <param name="PaymentAttemptId">The canonical PaymentAttempt identifier.</param>
/// <param name="ProviderCode">The provider code.</param>
/// <param name="ProviderProduct">The provider product code.</param>
/// <param name="ProviderSessionId">The provider-specific session identifier.</param>
/// <param name="ProviderReference">The provider reference, when available.</param>
/// <param name="SessionStatus">The normalized session status.</param>
/// <param name="RedirectUrl">The provider redirect URL, when applicable.</param>
/// <param name="ExpiresAtUtc">The provider expiry timestamp, when applicable.</param>
/// <param name="IdempotencyKey">The idempotency key used during creation.</param>
/// <param name="RequestPayloadJson">The serialized request payload for evidence persistence.</param>
/// <param name="ResponsePayloadJson">The serialized response payload for evidence persistence.</param>
/// <param name="CreatedAtUtc">The creation timestamp in UTC.</param>
public sealed record ProviderSessionRecord(
    Guid ProviderSessionRecordId,
    Guid PaymentAttemptId,
    string ProviderCode,
    string ProviderProduct,
    string ProviderSessionId,
    string? ProviderReference,
    string SessionStatus,
    string? RedirectUrl,
    DateTimeOffset? ExpiresAtUtc,
    string IdempotencyKey,
    string RequestPayloadJson,
    string ResponsePayloadJson,
    DateTimeOffset CreatedAtUtc);
