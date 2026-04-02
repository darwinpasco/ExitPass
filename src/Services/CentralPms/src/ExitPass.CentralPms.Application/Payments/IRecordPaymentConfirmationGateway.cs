namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Application layer depends on an abstraction, not a direct SQL call.
/// </summary>
public interface IRecordPaymentConfirmationGateway
{
    Task<RecordPaymentConfirmationResult> RecordAsync(
        RecordPaymentConfirmationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
