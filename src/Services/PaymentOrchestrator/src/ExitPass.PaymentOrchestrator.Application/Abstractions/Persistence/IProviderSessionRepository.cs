using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Persists provider session evidence records created by the Payment Orchestrator.
///
/// BRD:
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 9.2 Payments Domain
///
/// Invariants Enforced:
/// - Provider execution evidence must be persisted outside core payment truth.
/// </summary>
public interface IProviderSessionRepository
{
    /// <summary>
    /// Reserves durable ownership of provider-session initiation for a payment attempt and rail before any provider call.
    /// </summary>
    /// <param name="reservation">The provider-session initiation reservation to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reservation result, including any existing active provider-session record.</returns>
    Task<ProviderSessionInitiationReservationResult> TryReserveInitiationAsync(
        ProviderSessionInitiationReservation reservation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Completes a previously reserved provider-session initiation with the provider handoff result.
    /// </summary>
    /// <param name="providerSessionRecordId">The reserved provider-session record identifier.</param>
    /// <param name="record">The completed provider session record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task CompleteInitiationAsync(
        Guid providerSessionRecordId,
        ProviderSessionRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a provider session record.
    /// </summary>
    /// <param name="record">The provider session record to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(ProviderSessionRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a provider session record by provider code and provider session identifier.
    /// </summary>
    /// <param name="providerCode">The provider code.</param>
    /// <param name="providerSessionId">The provider session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching provider session record, or <c>null</c> if none exists.</returns>
    Task<ProviderSessionRecord?> FindByProviderSessionIdAsync(
        string providerCode,
        string providerSessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the latest active provider session for a Central PMS parking session.
    /// </summary>
    /// <param name="parkingSessionId">The canonical parking session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest resumable provider session record, or <c>null</c> if none exists.</returns>
    Task<ProviderSessionRecord?> FindLatestActiveByParkingSessionIdAsync(
        Guid parkingSessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the latest provider session evidence for a Central PMS payment attempt, including non-resumable records.
    /// </summary>
    /// <param name="paymentAttemptId">The canonical payment attempt identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest provider session record for the payment attempt, or <c>null</c> if none exists.</returns>
    Task<ProviderSessionRecord?> FindLatestByPaymentAttemptIdAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates persisted provider-session evidence from a verified provider webhook outcome.
    /// </summary>
    /// <param name="providerCode">The provider code.</param>
    /// <param name="providerSessionId">The provider session identifier.</param>
    /// <param name="providerReference">The terminal provider transaction/reference identifier, when supplied.</param>
    /// <param name="sessionStatus">The normalized provider session status.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task MarkWebhookOutcomeAsync(
        string providerCode,
        string providerSessionId,
        string? providerReference,
        string sessionStatus,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provider-session initiation reservation stored before a provider call so independent Payment Orchestrator instances converge.
/// </summary>
/// <param name="ProviderSessionRecordId">The reserved provider-session record identifier.</param>
/// <param name="PaymentAttemptId">The canonical payment attempt identifier.</param>
/// <param name="ProviderProduct">The selected provider rail/product code.</param>
/// <param name="IdempotencyKey">The stable provider initiation idempotency key.</param>
/// <param name="CorrelationId">The correlation identifier, when available.</param>
/// <param name="RequestPayloadJson">The safe serialized initiation request evidence.</param>
/// <param name="AmountMinorUnits">The requested provider amount in minor units.</param>
/// <param name="CurrencyCode">The requested provider currency.</param>
/// <param name="CreatedAtUtc">The reservation timestamp.</param>
public sealed record ProviderSessionInitiationReservation(
    Guid ProviderSessionRecordId,
    Guid PaymentAttemptId,
    string ProviderProduct,
    string IdempotencyKey,
    Guid? CorrelationId,
    string RequestPayloadJson,
    long AmountMinorUnits,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Result of attempting to reserve provider-session initiation.
/// </summary>
/// <param name="Outcome">The durable reservation outcome.</param>
/// <param name="ProviderSession">The existing or newly reserved provider-session record.</param>
public sealed record ProviderSessionInitiationReservationResult(
    ProviderSessionInitiationReservationOutcome Outcome,
    ProviderSessionRecord ProviderSession);

/// <summary>
/// Durable provider-session initiation reservation outcome.
/// </summary>
public enum ProviderSessionInitiationReservationOutcome
{
    /// <summary>
    /// This caller acquired the durable right to call the provider.
    /// </summary>
    Acquired,

    /// <summary>
    /// A durable provider session already exists and may be reused or classified by the caller.
    /// </summary>
    Existing
}
