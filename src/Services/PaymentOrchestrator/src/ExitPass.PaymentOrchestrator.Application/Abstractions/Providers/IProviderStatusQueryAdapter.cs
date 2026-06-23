using System.Threading;
using System.Threading.Tasks;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Defines the provider-specific status-query boundary for existing provider sessions.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Provider status evidence must remain provider-neutral before Central PMS can evaluate it.
/// - Provider status queries do not create platform payment finality, PaymentConfirmation, or ExitAuthorization.
/// </summary>
public interface IProviderStatusQueryAdapter
{
    /// <summary>
    /// Gets the provider code implemented by this status-query adapter.
    /// </summary>
    string ProviderCode { get; }

    /// <summary>
    /// Gets the provider product code implemented by this status-query adapter.
    /// </summary>
    string ProviderProduct { get; }

    /// <summary>
    /// Queries the provider for status evidence for a known provider session.
    /// </summary>
    /// <param name="command">The provider-scoped status-query command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Provider-neutral status-query evidence. This is not platform payment finality.</returns>
    Task<ProviderStatusQueryResult> QueryProviderSessionStatusAsync(
        ProviderStatusQueryCommand command,
        CancellationToken cancellationToken);
}
