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
