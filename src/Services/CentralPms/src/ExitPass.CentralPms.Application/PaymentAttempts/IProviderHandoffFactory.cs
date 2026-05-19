using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Domain.PaymentAttempts;

namespace ExitPass.CentralPms.Application.PaymentAttempts;

/// <summary>
/// Creates provider handoff details after Central PMS has accepted a payment attempt.
/// </summary>
public interface IProviderHandoffFactory
{
    /// <summary>
    /// Creates placeholder handoff details for the selected payment provider.
    /// </summary>
    /// <param name="provider">Provider or rail selected for the attempt.</param>
    /// <param name="paymentAttemptId">Payment attempt for which the handoff is generated.</param>
    /// <returns>Provider handoff details for the client response.</returns>
    ProviderHandoffResult CreatePlaceholder(PaymentProvider provider, Guid paymentAttemptId);
}
