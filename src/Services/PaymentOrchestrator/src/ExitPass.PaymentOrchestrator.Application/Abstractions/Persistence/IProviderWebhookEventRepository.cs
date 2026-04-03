using System.Threading;
using System.Threading.Tasks;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Persists immutable provider webhook event evidence for deduplication, audit, and traceability.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Duplicate provider callbacks must be detected deterministically.
/// - Raw provider webhook evidence must be persisted outside core payment truth.
/// </summary>
public interface IProviderWebhookEventRepository
{
    /// <summary>
    /// Determines whether a provider event has already been recorded.
    /// </summary>
    /// <param name="providerCode">The provider code.</param>
    /// <param name="providerEventId">The provider event identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the provider event has already been recorded; otherwise, <c>false</c>.
    /// </returns>
    Task<bool> ExistsByProviderEventIdAsync(
        string providerCode,
        string providerEventId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a provider webhook event record.
    /// </summary>
    /// <param name="record">The provider webhook event record to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(ProviderWebhookEventRecord record, CancellationToken cancellationToken);
}
