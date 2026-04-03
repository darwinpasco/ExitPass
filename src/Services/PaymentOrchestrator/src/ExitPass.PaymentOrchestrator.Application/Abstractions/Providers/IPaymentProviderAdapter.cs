using System.Threading;
using System.Threading.Tasks;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Defines the boundary for provider-specific payment behavior inside the Payment Orchestrator.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 4.2.7 Payment Orchestrator
///
/// Invariants Enforced:
/// - Provider-specific behavior must remain behind the adapter boundary.
/// - POA may initiate and verify provider interactions but must not finalize PaymentAttempt state.
/// </summary>
public interface IPaymentProviderAdapter
{
    /// <summary>
    /// Gets the provider code implemented by this adapter.
    /// </summary>
    string ProviderCode { get; }

    /// <summary>
    /// Gets the provider product code implemented by this adapter.
    /// </summary>
    string ProviderProduct { get; }

    /// <summary>
    /// Creates a provider payment session for an existing ExitPass PaymentAttempt.
    /// </summary>
    /// <param name="command">The provider session creation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created provider payment session result.</returns>
    Task<CreateProviderPaymentSessionResult> CreatePaymentSessionAsync(
        CreateProviderPaymentSessionCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies an inbound provider webhook request and normalizes it into a canonical outcome shape.
    /// </summary>
    /// <param name="request">The raw provider webhook request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The webhook verification result.</returns>
    Task<ProviderWebhookVerificationResult> VerifyWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken);
}
