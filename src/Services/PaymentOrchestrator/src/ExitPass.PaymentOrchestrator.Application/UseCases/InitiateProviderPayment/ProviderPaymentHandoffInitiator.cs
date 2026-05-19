using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Internal;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;

/// <summary>
/// Default provider handoff initiator that delegates to the provider-payment initiation use case.
/// </summary>
public sealed class ProviderPaymentHandoffInitiator : IProviderPaymentHandoffInitiator
{
    private readonly InitiateProviderPaymentHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderPaymentHandoffInitiator"/> class.
    /// </summary>
    /// <param name="handler">Provider payment initiation handler.</param>
    public ProviderPaymentHandoffInitiator(InitiateProviderPaymentHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <inheritdoc />
    public Task<InitiateProviderPaymentResponse> InitiateAsync(
        InitiateProviderPaymentRequest request,
        CancellationToken cancellationToken)
    {
        return _handler.HandleAsync(request, cancellationToken);
    }
}
