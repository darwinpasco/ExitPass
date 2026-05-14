namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Application layer depends on an abstraction, not a direct SQL call.
/// </summary>
public interface IRecordPaymentConfirmationGateway
{
    /// <summary>
    /// Records verified provider payment evidence before Central PMS finalizes the attempt.
    /// </summary>
    /// <param name="command">Provider confirmation evidence normalized for Central PMS.</param>
    /// <param name="now">Timestamp to persist as the verification time.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The persisted payment confirmation result.</returns>
    Task<RecordPaymentConfirmationResult> RecordAsync(
        RecordPaymentConfirmationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
