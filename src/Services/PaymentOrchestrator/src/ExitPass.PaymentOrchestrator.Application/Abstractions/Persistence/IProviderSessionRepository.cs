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
}
