namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Invalid callback payloads are rejected before persistence.
/// </summary>
public sealed class RecordPaymentConfirmationService
{
    private readonly IRecordPaymentConfirmationGateway _gateway;

    /// <summary>
    /// Creates the service that validates provider confirmation evidence before persistence.
    /// </summary>
    /// <param name="gateway">Gateway that records verified provider evidence.</param>
    public RecordPaymentConfirmationService(IRecordPaymentConfirmationGateway gateway)
    {
        _gateway = gateway;
    }

    /// <summary>
    /// Validates and records verified provider payment evidence for a payment attempt.
    /// </summary>
    /// <param name="command">Payment confirmation evidence normalized for Central PMS.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The persisted confirmation result.</returns>
    public async Task<RecordPaymentConfirmationResult> ExecuteAsync(
        RecordPaymentConfirmationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException("PaymentAttemptId is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ProviderReference))
        {
            throw new ArgumentException("ProviderReference is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ProviderStatus))
        {
            throw new ArgumentException("ProviderStatus is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            throw new ArgumentException("RequestedBy is required.", nameof(command));
        }

        if (command.AmountConfirmed.HasValue && string.IsNullOrWhiteSpace(command.CurrencyCode))
        {
            throw new ArgumentException("CurrencyCode is required when AmountConfirmed is supplied.", nameof(command));
        }

        return await _gateway.RecordAsync(
            command,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
}
