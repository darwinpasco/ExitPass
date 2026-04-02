namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Invalid callback payloads are rejected before persistence.
/// </summary>
public sealed class RecordPaymentConfirmationService
{
    private readonly IRecordPaymentConfirmationGateway _gateway;

    public RecordPaymentConfirmationService(IRecordPaymentConfirmationGateway gateway)
    {
        _gateway = gateway;
    }

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
