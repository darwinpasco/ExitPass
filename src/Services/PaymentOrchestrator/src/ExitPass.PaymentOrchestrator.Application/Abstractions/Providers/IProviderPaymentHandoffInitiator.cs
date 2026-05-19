using ExitPass.PaymentOrchestrator.Contracts.Internal;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Initiates provider handoff creation for an already-authoritative Central PMS PaymentAttempt.
/// </summary>
public interface IProviderPaymentHandoffInitiator
{
    /// <summary>
    /// Creates the provider-side handoff artifact for a payment attempt.
    /// </summary>
    /// <param name="request">Provider payment initiation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider handoff response.</returns>
    Task<InitiateProviderPaymentResponse> InitiateAsync(
        InitiateProviderPaymentRequest request,
        CancellationToken cancellationToken);
}
