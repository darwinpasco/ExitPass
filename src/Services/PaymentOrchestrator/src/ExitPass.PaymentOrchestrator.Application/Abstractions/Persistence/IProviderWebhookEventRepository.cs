using System.Threading;
using System.Threading.Tasks;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Persists immutable provider callback evidence for deduplication, audit, and traceability.
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
/// - Raw provider callback evidence must be persisted outside core payment truth.
/// </summary>
public interface IProviderWebhookEventRepository
{
    /// <summary>
    /// Checks whether a callback with the given provider event identifier already exists.
    /// </summary>
    /// <param name="providerCode">The provider code.</param>
    /// <param name="providerEventId">The provider callback/event identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the callback already exists; otherwise <c>false</c>.</returns>
    Task<bool> ExistsByProviderEventIdAsync(
        string providerCode,
        string providerEventId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists a provider callback evidence record.
    /// </summary>
    /// <param name="record">The callback evidence record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(
        ProviderWebhookEventRecord record,
        CancellationToken cancellationToken);
}
